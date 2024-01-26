using System;
using System.IO;
using System.Text;
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
        private readonly ILogger<WebSocketConnection> logger;
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

        public WebSocketConnection(Guid id, WebSocket webSocket, WebSocketConnectionsOptions options, ILogger<WebSocketConnection> logger)
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
            if (isDisposed)
            {
                return;
            }

            logger.LogDebug("[{id}] Cleaning up WS connection", Id);
            isDisposed = true;
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

        private async Task SendUntilCloseAsync(PipeReader reader, CancellationToken token)
        {
            try
            {
                byte[] empty = [];

                while (webSocket.State == WebSocketState.Open && !token.IsCancellationRequested && await reader.ReadAsync(token) is ReadResult result && !result.IsCompleted && !result.IsCanceled)
                {
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    if (buffer.IsSingleSegment)
                    {
                        await webSocket.SendAsync(buffer.First, WebSocketMessageType.Text, GetMessageFlags(true, !options.UseCompression), token);
                    }
                    else
                    {
                        SequencePosition position = buffer.Start;

                        while (buffer.TryGet(ref position, out var memory, advance: true))
                        {
                            await webSocket.SendAsync(memory, WebSocketMessageType.Text, GetMessageFlags(false, !options.UseCompression), token);
                        }

                        await webSocket.SendAsync(empty, WebSocketMessageType.Text, GetMessageFlags(true, !options.UseCompression), token);
                    }

                    reader.AdvanceTo(buffer.End, buffer.End);
                }
            }
            catch (OperationCanceledException) {}
            catch (WebSocketException wsex) when (wsex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                logger.LogError(wsex, "[{id}] Error occurred during receiving from WebSocket, closed prematurely", Id);
                isErrored = true;
                errorMessage = "The WebSocket was closed prematurely";
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
            {
                logger.LogError(ex, "[{id}] Error occurred during receiving from WebSocket", Id);
                isErrored = true;
                errorMessage = "Could not read from WebSocket";
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
            catch (OperationCanceledException) { }
            catch (WebSocketException wsex) when (wsex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                logger.LogError(wsex, "[{id}] Error occurred during receiving from WebSocket, closed prematurely", Id);
                isErrored = true;
                errorMessage = "The WebSocket was closed prematurely";
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
            {
                logger.LogError(ex, "[{id}] Error occurred during receiving from WebSocket", Id);
                isErrored = true;
                errorMessage = "Could not read from WebSocket";
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
