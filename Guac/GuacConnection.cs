using GLOKON.GuacWS.Server.Cipher;
using GLOKON.GuacWS.Server.Guac.Parameters;
using GLOKON.GuacWS.Server.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GLOKON.GuacWS.Server.Guac
{
    internal class GuacConnection
    {
        public const string PingOpCode = "ping";
        private const string InternalDataOpCode = "";

        private readonly WebSocketConnection webSocket;
        private readonly GuacDClient guacD;
        private readonly GuacOptions options;
        private readonly ILogger<GuacConnection> logger;
        private readonly SymmetricCipher cipher;
        private readonly GlobalStore store;
        private readonly ConcurrentQueue<byte[]> pendingWebSocketMessages = new();

        private string handshakeMessage = string.Empty;
        private bool hasActivity;
        private bool handshakeReplySent;
        private CancellationTokenSource cts;
        private bool sendPing = false;
        private bool isStopped = false;

        public Guid Id { get; private set; }
        public ConnectionType ProtocolType { get; private set; }
        public Dictionary<string, string> Settings { get; private set; } = [];

        public static string FormatProtocolMessage(string opCode, params string[] args)
        {
            // Guac Protocol Format of "OPCODE,ARG1,ARG2,ARG3,...;"
            if (args == null || args.Length == 0)
            {
                return FormatProtocolChunk(opCode) + ";";
            }

            string formattedArgs = string.Empty;
            if (args.Length == 1)
            {
                formattedArgs = FormatProtocolChunk(args[0]);
            }
            else
            {
                formattedArgs = string.Join(",", args.Select(FormatProtocolChunk).ToArray());
            }

            return FormatProtocolChunk(opCode) + "," + formattedArgs + ";";
        }

        public GuacConnection(Guid Id, WebSocketConnection webSocket, GuacDClient guacD, GuacOptions options, SymmetricCipher cipher, GlobalStore store, ILogger<GuacConnection> logger)
        {
            this.Id = Id;
            this.webSocket = webSocket;
            this.guacD = guacD;
            this.options = options;
            this.cipher = cipher;
            this.store = store;
            this.logger = logger;
        }

        public async Task StartAsync(IDictionary<string, StringValues> untrustedParams)
        {
            if (isStopped)
            {
                logger.LogWarning("[{id}] Attempting to start a stopped GuacWS connection", Id);
                return;
            }

            logger.LogInformation("[{id}] Starting GuacWS Connection", Id);

            cts = new CancellationTokenSource();

            ParseToken(untrustedParams);

            await guacD.ConnectAsync();

            // Send Tunnel ID to Client
            SendToWebSocket(FormatProtocolMessage(InternalDataOpCode, Id.ToString()));

            // Send initial GuacD message
            handshakeMessage = string.Empty;
            SendToGuacD(FormatProtocolMessage("select", ProtocolType.ToString().ToLower()));
            await FinishGuacDSendAsync(cts.Token).ConfigureAwait(false);

            logger.LogInformation("[{id}] Started GuacWS Connection", Id);

            StartActivityMonitor(options.PingFrequency, options.Timeout, cts.Token);
            await Task.WhenAny(
                guacD.RunUntilCloseAsync(cts.Token),
                ProcessGuacD(guacD.Input),
                webSocket.RunUntilCloseAsync(cts.Token),
                ProcessWebSocket(webSocket.Input));
        }

        public async Task StopAsync()
        {
            if (isStopped)
            {
                return;
            }

            logger.LogInformation("[{id}] Stopping GuacWS Connection", Id);

            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{id}] Failed to cancel guac connection gracefully", Id);
            }

            try
            {
                await guacD.CloseAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{id}] Error occured closing GuacD connection", Id);
            }

            try
            {
                await webSocket.CloseAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{id}] Error occured closing WebSocket connection", Id);
            }

            isStopped = true;

            if (Settings.TryGetValue("drive-path", out string userDrive))
            {
                DeleteUserDrive(userDrive);
            }

            logger.LogInformation("[{id}] Stopped GuacWS Connection", Id);
        }

        private static bool TryReadGuacDMessage(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> message)
        {
            SequencePosition? position = buffer.PositionOf(GuacDClient.DataDelimiter);

            if (position == null)
            {
                message = default;
                return false;
            }

            SequencePosition nextDataStart = buffer.GetPosition(1, position.Value);
            message = buffer.Slice(0, nextDataStart);
            buffer = buffer.Slice(nextDataStart);
            return true;
        }

        private static string FormatProtocolChunk(string chunk)
        {
            string finalChunk = chunk ?? string.Empty;
            return string.Format("{0}.{1}", finalChunk.Length, finalChunk);
        }

        private static string CreateUserDrive(string root, string userPath)
        {
            string finalUserPath = Path.Combine(root, userPath.TrimStart('/'));

            // Delete user path, start fresh
            DeleteUserDrive(finalUserPath);
            Directory.CreateDirectory(finalUserPath);

            return finalUserPath;
        }

        private static void DeleteUserDrive(string userPath)
        {
            if (Directory.Exists(userPath))
            {
                Directory.Delete(userPath, true);
            }
        }

        private async Task ProcessWebSocket(PipeReader reader)
        {
            try
            {
                while (await reader.ReadAsync(cts.Token) is ReadResult result && !result.IsCompleted && !result.IsCanceled)
                {
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    if (buffer.IsSingleSegment)
                    {
                        ReceiveWSToGuacD(buffer.First);
                    }
                    else
                    {
                        SequencePosition position = buffer.Start;
                        while (buffer.TryGet(ref position, out var memory, advance: true))
                        {
                            ReceiveWSToGuacD(memory);
                        }
                    }

                    FlushResult flushResult = await FinishGuacDSendAsync(cts.Token).ConfigureAwait(false);
                    if (flushResult.IsCanceled || flushResult.IsCompleted)
                    {
                        break;
                    }

                    reader.AdvanceTo(buffer.End, buffer.End);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
            {
                logger.LogError(ex, "[{id}] Error occurred during processing from WebSocket", Id);
                throw;
            }

            logger.LogDebug("[{id}] Finished processing from WebSocket", Id);
        }

        private async Task ProcessGuacD(PipeReader reader)
        {
            try
            {
                while (await reader.ReadAsync(cts.Token) is ReadResult result && !result.IsCompleted && !result.IsCanceled)
                {
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    while (TryReadGuacDMessage(ref buffer, out var messageBuffer))
                    {
                        if (messageBuffer.IsSingleSegment)
                        {
                            ReceiveGuacDToWS(messageBuffer.First);
                        }
                        else
                        {
                            SequencePosition position = messageBuffer.Start;

                            while (messageBuffer.TryGet(ref position, out var memory, advance: true))
                            {
                                ReceiveGuacDToWS(memory);
                            }
                        }
                    }

                    // Send ping if we have to
                    if (sendPing)
                    {
                        SendToWebSocket(store.PingData);
                        sendPing = false;
                    }

                    if (!handshakeReplySent && await TrySendGuacDHandshakeReplyAsync(handshakeMessage))
                    {
                        handshakeReplySent = true;
                        handshakeMessage = string.Empty;
                    }

                    FlushResult flushResult = await FinishWebSocketSendAsync(cts.Token).ConfigureAwait(false);
                    if (flushResult.IsCanceled || flushResult.IsCompleted)
                    {
                        break;
                    }

                    reader.AdvanceTo(buffer.Start, buffer.End);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
            {
                logger.LogError(ex, "[{id}] Error occurred during processing from GuacD", Id);
                throw;
            }

            logger.LogDebug("[{id}] Finished processing from GuacD", Id);
        }

        private void ReceiveWSToGuacD(ReadOnlyMemory<byte> message)
        {
            UpdateActivity();

            if (handshakeReplySent)
            {
                while (!pendingWebSocketMessages.IsEmpty)
                {
                    if (pendingWebSocketMessages.TryDequeue(out byte[] pendingMessage))
                    {
                        SendToGuacD(pendingMessage);
                    }
                }

                SendToGuacD(message);
            }
            else
            {
                pendingWebSocketMessages.Enqueue(message.ToArray());
            }
        }

        private void ReceiveGuacDToWS(ReadOnlyMemory<byte> message)
        {
            UpdateActivity();

            if (handshakeReplySent)
            {
                SendToWebSocket(message);
            }
            else
            {
                handshakeMessage += Encoding.UTF8.GetString(message.Span);
            }
        }

        private async Task<bool> TrySendGuacDHandshakeReplyAsync(string handshake)
        {
            logger.LogTrace("[{id}] GUAC Handshake: {handshake}", Id, handshake);

            if (!handshake.Contains(';'))
            {
                logger.LogTrace("[{id}] Received incomplete handshake: {handshake}", Id, handshake);
                return false;
            }

            SendToGuacD(FormatProtocolMessage("size", GetConnectionSetting("width"), GetConnectionSetting("height"), GetConnectionSetting("dpi")));
            SendToGuacD(FormatProtocolMessage("audio", GetConnectionSetting("audio")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
            SendToGuacD(FormatProtocolMessage("video", GetConnectionSetting("video")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
            SendToGuacD(FormatProtocolMessage("image", GetConnectionSetting("image")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
            SendToGuacD(FormatProtocolMessage("timezone", GetConnectionSetting("timezone")));

            string name = GetConnectionSetting("name");
            if (!string.IsNullOrEmpty(name))
            {
                SendToGuacD(FormatProtocolMessage("name", name));
            }

            string[] parameterRequests = handshake.TrimEnd(';').Split(',');
            List<string> parameterReplies = [];

            foreach (string  parameterRequest in parameterRequests)
            {
                string parameter = ParseOpCode(parameterRequest);
                if (parameter.StartsWith("VERSION_"))
                {
                    parameter = "protocol_version";
                }

                if (parameter != "args")
                {
                    // Ignore parameter
                    parameterReplies.Add(GetConnectionSetting(parameter));
                }
            }

            SendToGuacD(FormatProtocolMessage("connect", [.. parameterReplies]));
            await FinishGuacDSendAsync(cts.Token).ConfigureAwait(false);

            return true;
        }

        private string GetConnectionSetting(string parameter)
        {
            return Settings.GetValueOrDefault(parameter, null);
        }

        private string ParseOpCode(string parameter)
        {
            return parameter.Substring(parameter.IndexOf('.') + 1);
        }

        private GuacToken ParseToken(IDictionary<string, StringValues> untrustedParams)
        {
            if (!untrustedParams.TryGetValue("token", out StringValues values))
            {
                throw new ArgumentNullException("token", "Token is missing from the query string");
            }

            // Decrypt the token and parse it
            var serializeOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            string encryptedTokenValue = Encoding.UTF8.GetString(Convert.FromBase64String(values.ToString()));
            EncryptedToken parsedEncryptedToken = JsonSerializer.Deserialize<EncryptedToken>(encryptedTokenValue, serializeOptions);
            string tokenValue = cipher.Decrypt(Convert.FromBase64String(parsedEncryptedToken.Value), Convert.FromBase64String(parsedEncryptedToken.IV));
            GuacToken parsedToken = JsonSerializer.Deserialize<GuacToken>(tokenValue, serializeOptions);

            // Add parameters from the token
            ProtocolType = parsedToken.Connection.Type;
            parsedToken.Connection.Settings
                .ToList()
                .ForEach(param =>
                {
                    switch (param.Value.ValueKind)
                    {
                        case JsonValueKind.Null:
                            Settings.Add(param.Key, null);
                            break;
                        case JsonValueKind.False:
                            Settings.Add(param.Key, "false");
                            break;
                        case JsonValueKind.True:
                            Settings.Add(param.Key, "true");
                            break;
                        default:
                            Settings.Add(param.Key, param.Value.ToString());
                            break;
                    }
                });

            // Add parameters that are not in the token, if permitted
            HashSet<string> allowedUntrustedParams = options.AllowedParameters.Global;
            string connectionType = parsedToken.Connection.Type.ToString().ToLower();

            if (options.AllowedParameters.Connection.TryGetValue(connectionType, out HashSet<string> allowedConnParams))
            {
                allowedUntrustedParams.UnionWith(allowedConnParams);
            }

            untrustedParams
                .Where(param =>
                {
                    // Token is a reserved keyword
                    return param.Key != "token" && allowedUntrustedParams.Contains(param.Key);
                })
                .ToList()
                .ForEach(param => 
                {
                    Settings[param.Key] = param.Value.ToString();
                });

            if (Settings.TryGetValue("drive-path", out string userDrive))
            {
                Settings["drive-path"] = CreateUserDrive(options.UserDriveRoot, userDrive);
            }

            return parsedToken;
        }

        private void SendToGuacD(string message)
        {
            SendToGuacD(Encoding.UTF8.GetBytes(message));
        }

        private void SendToGuacD(ReadOnlyMemory<byte> message)
        {
            if (options.LogTraceMessages)
            {
                logger.LogTrace("[{id}] WS >> GUAC: {message}", Id, Encoding.UTF8.GetString(message.ToArray()));
            }

            guacD.Output.Write(message.Span);
        }

        private ValueTask<FlushResult> FinishGuacDSendAsync(CancellationToken cancellationToken)
        {
            return guacD.Output.FlushAsync(cancellationToken);
        }

        private void SendToWebSocket(string message)
        {
            SendToWebSocket(Encoding.UTF8.GetBytes(message));
        }

        private void SendToWebSocket(ReadOnlyMemory<byte> message)
        {
            if (options.LogTraceMessages)
            {
                logger.LogTrace("[{id}] GUAC >> WS: {message}", Id, Encoding.UTF8.GetString(message.ToArray()));
            }

            webSocket.Output.Write(message.Span);
        }

        private ValueTask<FlushResult> FinishWebSocketSendAsync(CancellationToken cancellationToken)
        {
            return webSocket.Output.FlushAsync(cancellationToken);
        }

        private void UpdateActivity()
        {
            hasActivity = true;
        }

        private void StartActivityMonitor(int pingFrequency, int timeout, CancellationToken token)
        {
            UpdateActivity(); // Initial hit to keep this alive

            Task.Run(async () =>
            {
                int trackedTime = timeout;
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(pingFrequency);
                    trackedTime -= pingFrequency;

                    if (trackedTime <= 0)
                    {
                        trackedTime = timeout;

                        if (!hasActivity)
                        {
                            logger.LogError("[{id}] Closing GuacWS connection, there was no activity within the specified timeout", Id);
                            await StopAsync();
                        }

                        hasActivity = false;
                    }

                    if (handshakeReplySent && !sendPing)
                    {
                        sendPing = true;
                    }
                }
            });
        }
    }
}
