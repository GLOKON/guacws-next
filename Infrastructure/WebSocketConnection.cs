using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using System.IO.Pipelines;
using System.Buffers;
using GLOKON.GuacWS.Server.Middlewares;

namespace GLOKON.GuacWS.Server.Infrastructure
{
    internal class WebSocketConnection : IDuplexPipe, IDisposable
    {
        private readonly ILogger logger;
        private readonly WebSocket webSocket;
        private readonly WebSocketConnectionsOptions options;
        private readonly Pipe inputPipe;
        private readonly Pipe outputPipe;

        private bool isClosed = false;
        private bool isDisposed = false;
        private bool isErrored = false;
        private string errorMessage = null;

        public Guid Id { get; }

        public PipeReader Input => inputPipe.Reader;

        public PipeWriter Output => outputPipe.Writer;

        public WebSocketCloseStatus? CloseStatus => webSocket.CloseStatus;

        public string? CloseStatusDescription => webSocket.CloseStatusDescription;

        public WebSocketState State => webSocket.State;

        public string? SubProtocol => webSocket.SubProtocol;

        public WebSocketConnection(Guid id, WebSocket webSocket, WebSocketConnectionsOptions options, ILogger logger)
        {
            Id = id;
            this.webSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
            this.options = options;
            this.logger = logger;

            inputPipe = new Pipe(new PipeOptions(useSynchronizationContext: false));
            outputPipe = new Pipe(new PipeOptions(useSynchronizationContext: false));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public async Task CloseAsync()
        {
            if (isClosed)
            {
                return;
            }

            logger.LogDebug("[{id}] Disconnecting WS connection", Id);

            using (var cancelCts = new CancellationTokenSource(options.CloseTimeout))
            {
                try
                {
                    await webSocket.CloseOutputAsync(isErrored ? WebSocketCloseStatus.InternalServerError : WebSocketCloseStatus.NormalClosure, errorMessage ?? string.Empty, cancelCts.Token);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[{id}] Error occured closing WebSocket gracefully", Id);
                }
            }

            isClosed = true;
        }

        public async Task RunUntilCloseAsync(CancellationToken token)
        {
            logger.LogDebug("[{id}] Using pipelines for WebSocket", Id);
            await Task.WhenAny(SendUntilCloseAsync(outputPipe.Reader, token), ReceiveUntilCloseAsync(inputPipe.Writer, token));
            logger.LogDebug("[{id}] Finished running the WebSocket", Id);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (isDisposed)
            {
                return;
            }

            logger.LogDebug("[{id}] Cleaning up WS connection", Id);
            isDisposed = true;
        }

        private async Task SendUntilCloseAsync(PipeReader reader, CancellationToken token)
        {
            try
            {
                while (webSocket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    ReadResult result = await reader.ReadAsync(token);
                    if (result.IsCompleted || result.IsCanceled)
                    {
                        break;
                    }

                    ReadOnlySequence<byte> buffer = result.Buffer;

                    if (buffer.IsSingleSegment)
                    {
                        await webSocket.SendAsync(buffer.First, WebSocketMessageType.Text, GetMessageFlags(true, !options.UseCompression), token);
                    }
                    else
                    {
                        // Mark the last segment itself as EndOfMessage instead of following up with a
                        // separate empty frame - avoids an extra WS frame/syscall on every multi-segment
                        // flush, which is common under load when GuacD produces faster than we drain it.
                        SequencePosition position = buffer.Start;
                        bool hasCurrent = buffer.TryGet(ref position, out ReadOnlyMemory<byte> current, advance: true);

                        while (hasCurrent)
                        {
                            bool hasNext = buffer.TryGet(ref position, out ReadOnlyMemory<byte> next, advance: true);

                            await webSocket.SendAsync(current, WebSocketMessageType.Text, GetMessageFlags(!hasNext, !options.UseCompression), token);

                            current = next;
                            hasCurrent = hasNext;
                        }
                    }

                    reader.AdvanceTo(buffer.End, buffer.End);
                }
            }
            catch (OperationCanceledException)
            {
                // Operation was cancelled, nothing to do
            }
            catch (WebSocketException wsex) when (wsex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                logger.LogError(wsex, "[{id}] Error occurred during sending to WebSocket, closed prematurely", Id);
                isErrored = true;
                errorMessage = "The WebSocket was closed prematurely";
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
            {
                if (token.IsCancellationRequested)
                {
                    // Expected: the connection is already tearing down (e.g. GuacConnection.StopAsync
                    // closing the WebSocket, or the receive loop reacting to a close frame) can put the
                    // WebSocket into a state where a SendAsync already in flight here throws. Not a real error.
                    logger.LogDebug(ex, "[{id}] WebSocket send loop ended during shutdown", Id);
                }
                else
                {
                    logger.LogError(ex, "[{id}] Error occurred during sending to WebSocket", Id);
                    isErrored = true;
                    errorMessage = "Could not send to WebSocket";
                }
            }

            await outputPipe.Reader.CompleteAsync();
            await outputPipe.Writer.CompleteAsync();
            outputPipe.Reset();
        }

        private async Task ReceiveUntilCloseAsync(PipeWriter writer, CancellationToken token)
        {
            try
            {
                while (webSocket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var message = await webSocket.ReceiveAsync(writer.GetMemory(options.ReceiveBufferSize), token);

                    while (!token.IsCancellationRequested && !message.EndOfMessage && message.MessageType != WebSocketMessageType.Close)
                    {
                        if (message.Count > 0)
                        {
                            writer.Advance(message.Count);
                            message = await webSocket.ReceiveAsync(writer.GetMemory(options.ReceiveBufferSize), token);
                        }
                        else
                        {
                            // End of stream
                            break;
                        }
                    }

                    // We didn't get a complete message, we can't flush partial message.
                    if (token.IsCancellationRequested || !message.EndOfMessage || message.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    writer.Advance(message.Count);

                    FlushResult result = await writer.FlushAsync(token).ConfigureAwait(false);
                    if (result.IsCompleted || result.IsCanceled)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Operation was cancelled, nothing to do
            }
            catch (WebSocketException wsex) when (wsex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                logger.LogError(wsex, "[{id}] Error occurred during receiving from WebSocket, closed prematurely", Id);
                isErrored = true;
                errorMessage = "The WebSocket was closed prematurely";
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
            {
                if (token.IsCancellationRequested)
                {
                    // Expected: the connection is already tearing down and SendUntilCloseAsync may have
                    // put the WebSocket into a state where a ReceiveAsync already in flight here throws.
                    logger.LogDebug(ex, "[{id}] WebSocket receive loop ended during shutdown", Id);
                }
                else
                {
                    logger.LogError(ex, "[{id}] Error occurred during receiving from WebSocket", Id);
                    isErrored = true;
                    errorMessage = "Could not read from WebSocket";
                }
            }

            await inputPipe.Writer.CompleteAsync();
            await inputPipe.Reader.CompleteAsync();
            inputPipe.Reset();
        }

        private static WebSocketMessageFlags GetMessageFlags(bool endOfMessage, bool disableCompression)
        {
            WebSocketMessageFlags messageFlags = WebSocketMessageFlags.None;

            if (endOfMessage)
            {
                messageFlags |= WebSocketMessageFlags.EndOfMessage;
            }

            if (disableCompression)
            {
                messageFlags |= WebSocketMessageFlags.DisableCompression;
            }

            return messageFlags;
        }
    }
}
