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
        private readonly PipeWriter outputWriter;

        public Guid Id { get; }

        public PipeReader Input => inputPipe.Reader;

        public PipeWriter Output => outputWriter;

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
            outputWriter = PipeWriter.Create(new WebSocketStream(webSocket, WebSocketMessageType.Text, options.UseCompression));
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
            await inputPipe.Writer.CompleteAsync();
            await inputPipe.Reader.CompleteAsync();
            inputPipe.Reset();

            using (var cts = new CancellationTokenSource(options.CloseTimeout))
            {
                await webSocket.CloseOutputAsync(closeStatus ?? WebSocketCloseStatus.NormalClosure, closeStatusDescription ?? string.Empty, cts.Token);
            }
        }

        public async Task RunUntilCloseAsync()
        {
            logger.LogDebug("[{0}] Using pipelines for WebSocket", Id);
            bool isErrored = false;

            while (webSocket.State == WebSocketState.Open && !cts.IsCancellationRequested)
            {
                try
                {
                    var message = await webSocket.ReceiveAsync(inputPipe.Writer.GetMemory(options.ReceiveBufferSize), cts.Token);

                    while (!cts.IsCancellationRequested && !message.EndOfMessage && message.MessageType != WebSocketMessageType.Close)
                    {
                        if (message.Count > 0)
                        {
                            inputPipe.Writer.Advance(message.Count);
                            message = await webSocket.ReceiveAsync(inputPipe.Writer.GetMemory(options.ReceiveBufferSize), cts.Token);
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

                    inputPipe.Writer.Advance(message.Count);

                    FlushResult result = await inputPipe.Writer.FlushAsync();

                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException wsex) when (wsex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                {
                    logger.LogError(wsex, "[{0}] Error occurred during receiving from WebSocket", Id);
                    isErrored = true;
                    break;
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
                {
                    logger.LogError(ex, "[{0}] Error occurred during receiving from WebSocket", Id);
                    isErrored = true;
                    break;
                }
            }

            if (isErrored)
            {
                await CloseAsync(WebSocketCloseStatus.InternalServerError, "There was a problem receiving data from the websocket");
            }
            else
            {
                await CloseAsync();
            }
        }
    }
}
