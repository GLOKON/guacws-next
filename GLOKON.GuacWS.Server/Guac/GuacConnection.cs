using GLOKON.GuacWS.Server.Infrastructure;
using GLOKON.GuacWS.Server.Infrastructure.Token;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GLOKON.GuacWS.Server.Guac
{
    internal class GuacConnection : IGuacConnection
    {
        private readonly WebSocketConnection webSocket;
        private readonly GuacDClient guacD;
        private readonly GuacOptions options;
        private readonly ILogger logger;
        private readonly GlobalStore store;
        private readonly ConcurrentQueue<byte[]> pendingWebSocketMessages = new();

        private string handshakeMessage = string.Empty;
        private bool hasActivity;
        private bool handshakeReplySent;
        private CancellationTokenSource cts;
        private bool sendPing = false;
        private bool isStopped = false;

        public Guid Id { get; private set; }

        public ConnectionProfile ConnectionProfile { get; private set; }

        public string UserDrive { get; private set; }

        public GuacConnection(Guid id, WebSocketConnection webSocket, GuacDClient guacD, GuacOptions options, GlobalStore store, ILogger logger)
        {
            Id = id;
            this.webSocket = webSocket;
            this.guacD = guacD;
            this.options = options;
            this.store = store;
            this.logger = logger;
        }

        public async Task StartAsync(ConnectionProfile profile)
        {
            if (isStopped)
            {
                logger.LogWarning("[{id}] Attempting to start a stopped GuacWS connection", Id);
                return;
            }

            logger.LogInformation("[{id}] Starting GuacWS Connection", Id);

            ConnectionProfile = profile;

            if (ConnectionProfile.Settings.TryGetValue("enable-drive", out string enableDriveStr) && bool.TryParse(enableDriveStr, out bool enableDrive) && enableDrive)
            {
                UserDrive = CreateUserDrive(Path.Combine(options.UserDriveRoot, Id.ToString()));
                ConnectionProfile.Settings["drive-path"] = UserDrive;
                ConnectionProfile.Settings["create-drive-path"] = "true";
            }

            cts = new CancellationTokenSource();

            await guacD.ConnectAsync();

            // Send Tunnel ID to Client
            SendToWebSocket(GuacProtocol.FormatProtocolMessage(GuacProtocol.InternalDataOpCode, Id.ToString()));

            // Send initial GuacD message
            handshakeMessage = string.Empty;

            // By default select the protocol, if the profile specifies an existing connection, use that instead
            string connectionToSelect = ConnectionProfile.Type.ToString().ToLower();
            if (!string.IsNullOrEmpty(ConnectionProfile.ExistingConnectionId))
            {
                connectionToSelect = ConnectionProfile.ExistingConnectionId;
            }

            SendToGuacD(GuacProtocol.FormatProtocolMessage("select", connectionToSelect));
            await FinishGuacDSendAsync(cts.Token).ConfigureAwait(false);

            logger.LogInformation("[{id}] Started GuacWS Connection", Id);

            StartActivityMonitor(options.PingFrequency, options.Timeout, cts.Token);
            await Task.WhenAny(
                guacD.RunUntilCloseAsync(cts.Token),
                ProcessGuacDAsync(guacD.Input),
                webSocket.RunUntilCloseAsync(cts.Token),
                ProcessWebSocketAsync(webSocket.Input));
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
                // Deliberately not disposing cts here: StartAsync's Task.WhenAny returns as soon as
                // any one of the four pump tasks ends, so the others can still be mid-iteration and
                // re-read cts.Token on their next loop pass (CancellationTokenSource.Token throws
                // ObjectDisposedException once disposed, even for tokens already handed out). This CTS
                // is never given a timeout, so it holds nothing worth reclaiming - leave it for the GC.
                cts?.Cancel();
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

            if (!string.IsNullOrEmpty(UserDrive))
            {
                DeleteUserDrive(UserDrive);
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

        private static string CreateUserDrive(string userPath)
        {
            // Delete user path, start fresh
            DeleteUserDrive(userPath);
            Directory.CreateDirectory(userPath);

            return userPath;
        }

        private static void DeleteUserDrive(string userPath)
        {
            if (Directory.Exists(userPath))
            {
                Directory.Delete(userPath, true);
            }
        }

        private async Task ProcessWebSocketAsync(PipeReader reader)
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    ReadResult result = await reader.ReadAsync(cts.Token);
                    if (result.IsCompleted || result.IsCanceled)
                    {
                        break;
                    }

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
            catch (OperationCanceledException)
            {
                // Operation was cancelled, nothing to do
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
            {
                if (cts.IsCancellationRequested)
                {
                    // Expected: the connection is already tearing down and guacD's own pump/close may
                    // have completed guacD.Output out from under a write we had in flight. Not a real error.
                    logger.LogDebug(ex, "[{id}] WebSocket processing loop ended during shutdown", Id);
                }
                else
                {
                    logger.LogError(ex, "[{id}] Error occurred during processing from WebSocket", Id);
                    throw;
                }
            }

            logger.LogDebug("[{id}] Finished processing from WebSocket", Id);
        }

        private async Task ProcessGuacDAsync(PipeReader reader)
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    ReadResult result = await reader.ReadAsync(cts.Token);
                    if (result.IsCompleted)
                    {
                        break;
                    }

                    // A cancellation without the connection itself being torn down means we were
                    // woken deliberately (CancelPendingRead from the activity monitor) to flush a
                    // pending ping while GuacD is idle - fall through and loop back around instead
                    // of tearing down the connection.
                    if (result.IsCanceled && cts.IsCancellationRequested)
                    {
                        break;
                    }

                    ReadOnlySequence<byte> buffer = result.Buffer;

                    if (handshakeReplySent)
                    {
                        // Fast path: once the handshake is complete we no longer need to know
                        // individual instruction boundaries, so forward the raw bytes as-is
                        // instead of scanning for delimiters and writing one instruction at a time.
                        if (buffer.IsSingleSegment)
                        {
                            ReceiveGuacDToWS(buffer.First);
                        }
                        else
                        {
                            SequencePosition position = buffer.Start;

                            while (buffer.TryGet(ref position, out var memory, advance: true))
                            {
                                ReceiveGuacDToWS(memory);
                            }
                        }

                        buffer = buffer.Slice(buffer.End);
                    }
                    else
                    {
                        // Slow path: only needed prior to handshake completion, where we must
                        // buffer up individual instructions to detect the full handshake string.
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
                    }

                    // Send ping if we have been signalled
                    if (sendPing)
                    {
                        SendPingToWebSocket();
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
            catch (OperationCanceledException)
            {
                // Operation was cancelled, nothing to do
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
            {
                if (cts.IsCancellationRequested)
                {
                    // Expected: the connection is already tearing down and the WebSocket's own pump/close
                    // may have completed webSocket.Output out from under a write we had in flight.
                    logger.LogDebug(ex, "[{id}] GuacD processing loop ended during shutdown", Id);
                }
                else
                {
                    logger.LogError(ex, "[{id}] Error occurred during processing from GuacD", Id);
                    throw;
                }
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

            SendToGuacD(GuacProtocol.FormatProtocolMessage("size", GetConnectionSetting("width"), GetConnectionSetting("height"), GetConnectionSetting("dpi")));
            SendToGuacD(GuacProtocol.FormatProtocolMessage("audio", GetConnectionSetting("audio")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
            SendToGuacD(GuacProtocol.FormatProtocolMessage("video", GetConnectionSetting("video")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
            SendToGuacD(GuacProtocol.FormatProtocolMessage("image", GetConnectionSetting("image")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
            SendToGuacD(GuacProtocol.FormatProtocolMessage("timezone", GetConnectionSetting("timezone")));

            string name = GetConnectionSetting("name");
            if (!string.IsNullOrEmpty(name))
            {
                SendToGuacD(GuacProtocol.FormatProtocolMessage("name", name));
            }

            string[] parameterRequests = handshake.TrimEnd(';').Split(',');
            List<string> parameterReplies = [];

            foreach (string  parameterRequest in parameterRequests)
            {
                string parameter = GuacProtocol.GetData(parameterRequest);
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

            SendToGuacD(GuacProtocol.FormatProtocolMessage("connect", [.. parameterReplies]));
            await FinishGuacDSendAsync(cts.Token).ConfigureAwait(false);

            return true;
        }

        private string GetConnectionSetting(string parameter)
        {
            return ConnectionProfile.Settings.GetValueOrDefault(parameter, null);
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

        private void SendPingToWebSocket()
        {
            SendToWebSocket(store.PingData);
            sendPing = false;
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
                    try
                    {
                        await Task.Delay(pingFrequency, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

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

                    // Always route the actual write through ProcessGuacDAsync's pump thread rather
                    // than writing to webSocket.Output here directly - Pipelines only supports a
                    // single writer at a time, and that loop also flushes webSocket.Output on every
                    // iteration (including pre-handshake), so writing from this thread too would race it.
                    if (!sendPing)
                    {
                        sendPing = true;

                        // GuacD may be fully idle, in which case ProcessGuacDAsync would stay
                        // blocked on reader.ReadAsync() and never flush this ping. CancelPendingRead
                        // wakes it up without completing/closing the pipe so it can send the ping
                        // and resume reading.
                        guacD.Input.CancelPendingRead();
                    }
                }
            }, CancellationToken.None); // We do not need to cancel it, as loop will exit anyway
        }
    }
}
