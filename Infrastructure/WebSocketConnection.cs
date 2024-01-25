using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using System.IO.Pipelines;
using Microsoft.Extensions.Options;
using System.Buffers;
using GLOKON.GuacWS.Server.Middlewares;

namespace GLOKON.GuacWS.Server.Infrastructure
{
    internal class WebSocketConnection : IDuplexPipe, IDisposable
    {
        private readonly ILogger<WebSocketConnection> logger;
        private readonly WebSocket webSocket;
        private readonly WebSocketConnectionsOptions options;
        private readonly CancellationTokenSource cts;
        private readonly Pipe inputPipe;
        private readonly Pipe outputPipe;

        public Guid Id { get; }

        public PipeReader Input => inputPipe.Reader;

        public PipeWriter Output => outputPipe.Writer;

        public WebSocketCloseStatus? CloseStatus => webSocket.CloseStatus;

        public string? CloseStatusDescription => webSocket.CloseStatusDescription;

        public WebSocketState State => webSocket.State;

        public string? SubProtocol => webSocket.SubProtocol;

        public WebSocketConnection(Guid Id, WebSocket webSocket, WebSocketConnectionsOptions options, ILogger<WebSocketConnection> logger)
        {
            this.Id = Id;
            this.webSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
            this.options = options;
            this.logger = logger;
            cts = new CancellationTokenSource();

            inputPipe = new Pipe(new PipeOptions(useSynchronizationContext: false));
            outputPipe = new Pipe(new PipeOptions(useSynchronizationContext: false));
        }

        public void Dispose()
        {
            logger.LogDebug("[{0}] Cleaning up WS connection", Id);
            cts.Dispose();
        }

        public async Task CloseAsync(WebSocketCloseStatus? closeStatus = null, string? closeStatusDescription = null)
        {
            logger.LogDebug("[{0}] Disconnecting WS connection", Id);
            cts.Cancel();

            using (var cts = new CancellationTokenSource(options.CloseTimeout))
            {
                await webSocket.CloseOutputAsync(closeStatus ?? WebSocketCloseStatus.NormalClosure, closeStatusDescription ?? string.Empty, cts.Token);
            }
        }

        public async Task RunUntilCloseAsync()
        {
            logger.LogDebug("[{0}] Using pipelines for WebSocket", Id);
            Task<bool> duplexTasks = await Task.WhenAny(SendUntilCloseAsync(outputPipe.Reader), ReceiveUntilCloseAsync(inputPipe.Writer));
            bool isErrored = await duplexTasks;

            if (isErrored)
            {
                await CloseAsync(WebSocketCloseStatus.InternalServerError, "There was a problem receiving data from the websocket");
            }
            else
            {
                await CloseAsync();
            }
        }

        private async Task<bool> SendUntilCloseAsync(PipeReader reader)
        {
            bool isErrored = false;

            try
            {
                byte[] empty = Array.Empty<byte>();

                while (webSocket.State == WebSocketState.Open && await reader.ReadAsync(cts.Token) is ReadResult result && !result.IsCompleted && !result.IsCanceled)
                {
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    if (buffer.IsSingleSegment)
                    {
                        await webSocket.SendAsync(buffer.First, WebSocketMessageType.Text, GetMessageFlags(true, !options.UseCompression), cts.Token);
                    }
                    else
                    {
                        SequencePosition position = buffer.Start;

                        while (buffer.TryGet(ref position, out var memory, advance: true))
                        {
                            await webSocket.SendAsync(memory, WebSocketMessageType.Text, GetMessageFlags(false, !options.UseCompression), cts.Token);
                        }

                        await webSocket.SendAsync(empty, WebSocketMessageType.Text, GetMessageFlags(true, !options.UseCompression), cts.Token);
                    }

                    reader.AdvanceTo(buffer.End, buffer.End);
                }
            }
            catch (OperationCanceledException) {}
            catch (WebSocketException wsex) when (wsex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                logger.LogError(wsex, "[{0}] Error occurred during receiving from WebSocket", Id);
                isErrored = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
            {
                logger.LogError(ex, "[{0}] Error occurred during receiving from WebSocket", Id);
                isErrored = true;
            }

            await outputPipe.Reader.CompleteAsync();
            await outputPipe.Writer.CompleteAsync();
            outputPipe.Reset();

            return isErrored;
        }

        private async Task<bool> ReceiveUntilCloseAsync(PipeWriter writer)
        {
            bool isErrored = false;

            try
            {
                while (webSocket.State == WebSocketState.Open && !cts.IsCancellationRequested)
                {
                    var message = await webSocket.ReceiveAsync(writer.GetMemory(options.ReceiveBufferSize), cts.Token);

                    while (!cts.IsCancellationRequested && !message.EndOfMessage && message.MessageType != WebSocketMessageType.Close)
                    {
                        if (message.Count > 0)
                        {
                            writer.Advance(message.Count);
                            message = await webSocket.ReceiveAsync(writer.GetMemory(options.ReceiveBufferSize), cts.Token);
                        }
                        else
                        {
                            break;
                        }
                    }

                    // We didn't get a complete message, we can't flush partial message.
                    if (cts.IsCancellationRequested || !message.EndOfMessage || message.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    writer.Advance(message.Count);

                    FlushResult result = await writer.FlushAsync(cts.Token).ConfigureAwait(false);
                    if (result.IsCompleted || result.IsCanceled)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException wsex) when (wsex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                logger.LogError(wsex, "[{0}] Error occurred during receiving from WebSocket", Id);
                isErrored = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
            {
                logger.LogError(ex, "[{0}] Error occurred during receiving from WebSocket", Id);
                isErrored = true;
            }

            await inputPipe.Writer.CompleteAsync();
            await inputPipe.Reader.CompleteAsync();
            inputPipe.Reset();

            return isErrored;
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
