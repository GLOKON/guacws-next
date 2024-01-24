using GLOKON.GuacWS.Server.Cipher;
using GLOKON.GuacWS.Server.Guac.Parameters;
using GLOKON.GuacWS.Server.Infrastructure;
using GLOKON.GuacWS.Server.Middlewares;
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
        private readonly WebSocketConnection webSocket;
        private readonly GuacDClient guacD;
        private readonly GuacOptions options;
        private readonly ILogger<GuacConnection> logger;
        private readonly SymmetricCipher cipher;
        private readonly ConcurrentQueue<byte[]> pendingWebSocketMessages = new ConcurrentQueue<byte[]>();

        private string handshakeMessage = string.Empty;
        private bool hasActivity;
        private bool handshakeReplySent;
        private CancellationTokenSource cts;

        public ConnectionType ProtocolType { get; private set; }
        public Dictionary<string, string> Settings { get; private set; } = new Dictionary<string, string>();

        public GuacConnection(WebSocketConnection webSocket, GuacDClient guacD, GuacOptions options, SymmetricCipher cipher, ILogger<GuacConnection> logger) {
            this.webSocket = webSocket;
            this.guacD = guacD;
            this.options = options;
            this.cipher = cipher;
            this.logger = logger;
        }

        public void Dispose()
        {
            guacD.Dispose();
            webSocket.Dispose();
        }

        public async Task StartAsync(IDictionary<string, StringValues> untrustedParams)
        {
            logger.LogDebug("[{0}] Starting GuacWS Connection", guacD.Id);

            ParseToken(untrustedParams);

            await guacD.ConnectAsync();

            StartActivityMonitor(options.Timeout);

            // Send initial message
            handshakeMessage = string.Empty;
            await SendOpCodeAsync(new string[] { "select", ProtocolType.ToString().ToLower() });
            await FinishGuacDSendAsync(cts.Token);

            logger.LogDebug("[{0}] Started GuacWS Connection", guacD.Id);

            await Task.WhenAny(
                guacD.RunUntilCloseAsync(),
                ProcessGuacD(guacD.Input),
                webSocket.RunUntilCloseAsync(),
                ProcessWebSocket(webSocket.Input));
        }

        public async Task StopAsync(bool isErrored = false, string? message = null)
        {
            logger.LogDebug("[{0}] Stopping GuacWS Connection", guacD.Id);

            StopActivityMonitor();

            await guacD.CloseAsync();

            if (isErrored)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, message);
            }
            else
            {
                await webSocket.CloseAsync();
            }

            if (Settings.TryGetValue("drive-path", out string userDrive))
            {
                DeleteUserDrive(userDrive);
            }

            logger.LogDebug("[{0}] Stopped GuacWS Connection", guacD.Id);
        }

        private string CreateUserDrive(string userPath)
        {
            string finalUserPath = Path.Combine(options.UserDriveRoot, userPath.TrimStart('/'));

            // Delete user path, start fresh
            DeleteUserDrive(finalUserPath);
            Directory.CreateDirectory(finalUserPath);

            return finalUserPath;
        }

        private void DeleteUserDrive(string userPath)
        {
            if (Directory.Exists(userPath))
            {
                Directory.Delete(userPath, true);
            }
        }

        private async Task ProcessWebSocket(PipeReader reader)
        {
            while (await reader.ReadAsync(cts.Token) is ReadResult result && !result.IsCompleted && !result.IsCanceled)
            {
                try
                {
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    try
                    {
                        if (buffer.IsSingleSegment)
                        {
                            await ReceiveWSToGuacDAsync(buffer.First);
                        }
                        else
                        {
                            SequencePosition position = buffer.Start;
                            while (buffer.TryGet(ref position, out var memory, advance: true))
                            {
                                await ReceiveWSToGuacDAsync(memory);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "[{0}] There was a problem handling the WebSocket message", guacD.Id);
                    }

                    FlushResult flushResult = await FinishGuacDSendAsync(cts.Token);
                    if (flushResult.IsCanceled || flushResult.IsCompleted)
                    {
                        break;
                    }

                    reader.AdvanceTo(buffer.End, buffer.End);
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
                {
                    logger.LogError(ex, "[{0}] Error occurred during processing from WebSocket", guacD.Id);
                    break;
                }
            }
        }

        private async Task ProcessGuacD(PipeReader reader)
        {
            while (await reader.ReadAsync(cts.Token) is ReadResult result && !result.IsCompleted && !result.IsCanceled)
            {
                try
                {
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    try
                    {
                        while (TryReadGuacDMessage(ref buffer, out var messageBuffer))
                        {
                            if (messageBuffer.IsSingleSegment)
                            {
                                await ReceiveGuacDToWSAsync(messageBuffer.First);
                            }
                            else
                            {
                                SequencePosition position = messageBuffer.Start;

                                while (messageBuffer.TryGet(ref position, out var memory, advance: true))
                                {
                                    await ReceiveGuacDToWSAsync(memory);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "[{0}] There was a problem handling the GuacD message", guacD.Id);
                    }

                    if (!handshakeReplySent && await TrySendHandshakeReplyAsync(handshakeMessage))
                    {
                        handshakeReplySent = true;
                        handshakeMessage = string.Empty;
                    }

                    FlushResult flushResult = await FinishWebSocketSendAsync(cts.Token);
                    if (flushResult.IsCanceled || flushResult.IsCompleted)
                    {
                        break;
                    }

                    reader.AdvanceTo(buffer.Start, buffer.End);
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
                {
                    logger.LogError(ex, "[{0}] Error occurred during processing from GuacD", guacD.Id);
                    break;
                }
            }
        }

        private bool TryReadGuacDMessage(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> message)
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

        private async Task ReceiveWSToGuacDAsync(ReadOnlyMemory<byte> message)
        {
            UpdateActivity();

            if (handshakeReplySent)
            {
                while (pendingWebSocketMessages.Count > 0)
                {
                    if (pendingWebSocketMessages.TryDequeue(out byte[] pendingMessage))
                    {
                        await SendToGuacDAsync(pendingMessage);
                    }
                }

                await SendToGuacDAsync(message);
            }
            else
            {
                pendingWebSocketMessages.Enqueue(message.ToArray());
            }
        }

        private Task ReceiveGuacDToWSAsync(ReadOnlyMemory<byte> message)
        {
            UpdateActivity();

            if (handshakeReplySent)
            {
                return SendToWebSocketAsync(message);
            }
            else
            {
                handshakeMessage += Encoding.UTF8.GetString(message.Span);
                return Task.CompletedTask;
            }
        }

        private async Task<bool> TrySendHandshakeReplyAsync(string handshake)
        {
            logger.LogTrace("[{0}] GUAC Handshake: {1}", webSocket.Id, handshake);

            if (handshake.IndexOf(';') == -1)
            {
                logger.LogTrace("[{0}] Received incomplete handshake: {1}", webSocket.Id, handshake);
                return false;
            }

            await SendOpCodeAsync(new string[] {
                "size",
                GetConnectionSetting("width"),
                GetConnectionSetting("height"),
                GetConnectionSetting("dpi")
            });

            await SendOpCodeAsync(new string[] { "audio", GetConnectionSetting("audio") });
            await SendOpCodeAsync(new string[] { "video", GetConnectionSetting("video") });
            await SendOpCodeAsync(new string[] { "image", GetConnectionSetting("image") });

            string[] parameterRequests = handshake.Split(',');
            IList<string> parameterReplies = new List<string>();

            foreach (string  parameterRequest in parameterRequests)
            {
                string parameter = ParseOpCode(parameterRequest);
                if (parameter.StartsWith("VERSION_"))
                {
                    parameter = "protocol_version";
                }

                if (parameter == "args")
                {
                    parameterReplies.Add("connect");
                }
                else
                {
                    parameterReplies.Add(GetConnectionSetting(parameter));
                }
            }

            await SendOpCodeAsync(parameterReplies.ToArray());
            await FinishGuacDSendAsync(cts.Token);

            return true;
        }

        private string GetConnectionSetting(string parameter)
        {
            return Settings.GetValueOrDefault(parameter, null);
        }

        private string ParseOpCode(string parameter)
        {
            return parameter.Substring(parameter.IndexOf(".") + 1);
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
            ISet<string> allowedUntrustedParams = options.AllowedParameters.Global;
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
                Settings["drive-path"] = CreateUserDrive(userDrive);
            }

            return parsedToken;
        }

        private Task SendOpCodeAsync(string[] parameters)
        {
            string formattedOpCode = FormatOpCode(parameters);
            if (options.LogTraceMessages)
            {
                logger.LogTrace("[{0}] Sending Guac Operation: {1}", guacD.Id, formattedOpCode);
            }

            return SendToGuacDAsync(Encoding.UTF8.GetBytes(formattedOpCode));
        }

        private string FormatOpCode(string[] opCodeParts)
        {
            string[] formattedOpCodes = opCodeParts.Select(opCodePart =>
            {
                string opCode = opCodePart ?? string.Empty;

                return string.Format("{0}.{1}", opCode.Length, opCode);
            }).ToArray();

            return string.Join(",", formattedOpCodes) + ";";
        }

        private async Task SendToGuacDAsync(ReadOnlyMemory<byte> message)
        {
            try
            {
                if (options.LogTraceMessages)
                {
                    logger.LogTrace("[{0}] WS >> GUAC: {1}", guacD.Id, Encoding.UTF8.GetString(message.ToArray()));
                }

                guacD.Output.Write(message.Span);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{0}] Problem sending to GuacD", guacD.Id);
                await StopAsync(true, "There was a problem sending to GuacD");
            }
        }

        private ValueTask<FlushResult> FinishGuacDSendAsync(CancellationToken cancellationToken)
        {
            return guacD.Output.FlushAsync(cancellationToken);
        }

        private async Task SendToWebSocketAsync(ReadOnlyMemory<byte> message)
        {
            try
            {
                if (options.LogTraceMessages)
                {
                    logger.LogTrace("[{0}] GUAC >> WS: {1}", webSocket.Id, Encoding.UTF8.GetString(message.ToArray()));
                }

                webSocket.Output.Write(message.Span);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{0}] Problem sending to WebSocket", webSocket.Id);
                await StopAsync(true, "There was a problem sending to WebSocket");
            }
        }

        private ValueTask<FlushResult> FinishWebSocketSendAsync(CancellationToken cancellationToken)
        {
            return webSocket.Output.FlushAsync(cancellationToken);
        }

        private void UpdateActivity()
        {
            hasActivity = true;
        }

        private void StartActivityMonitor(int timeout)
        {
            if (cts != null)
            {
                return;
            }

            cts = new CancellationTokenSource();
            UpdateActivity();

            Task.Run(async () =>
            {
                while (cts != null && !cts.IsCancellationRequested)
                {
                    await Task.Delay(timeout);

                    if (!hasActivity)
                    {
                        await StopAsync(true, "There was no activity within the specified timeout");
                    }

                    hasActivity = false;
                }
            });
        }

        private void StopActivityMonitor()
        {
            try
            {
                cts?.Cancel();
                cts?.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{0}] Failed to stop GuacWS activity monitor", guacD.Id);
            }
            finally
            {
                cts = null;
            }
        }
    }
}
