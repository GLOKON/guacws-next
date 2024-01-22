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

namespace GLOKON.GuacWS.Server.Infrastructure
{
    internal class WebSocketConnection: IDisposable
    {
        private readonly int _receiveBufferSize;
        private readonly int _sendSegmentSize;
        private readonly ILogger<WebSocketConnection> logger;
        private readonly WebSocket _webSocket;
        private readonly bool _useCompression;
        private readonly bool _usePipelines;
        private readonly CancellationTokenSource cts;

        public Guid Id { get; }
        public WebSocketCloseStatus? CloseStatus { get; private set; } = null;
        public string CloseStatusDescription { get; private set; } = null;


        public delegate Task ReceiveAsync<T>(T e);
        public ReceiveAsync<string> ReceiveTextAsync;
        public ReceiveAsync<byte[]> ReceiveBinaryAsync;

        public WebSocketConnection(Guid Id, WebSocket webSocket, bool useCompression, bool usePipelines, int sendSegmentSize, int receivePayloadBufferSize, ILogger<WebSocketConnection> logger)
        {
            this.Id = Id;
            _webSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
            _useCompression = useCompression;
            _usePipelines = usePipelines;
            _sendSegmentSize = sendSegmentSize;
            _receiveBufferSize = receivePayloadBufferSize;
            this.logger = logger;
            cts = new CancellationTokenSource();
        }

        public void Dispose()
        {
            logger.LogDebug("[{0}] Cleaning up WS connection", Id);
            cts.Cancel();
            cts.Dispose();
        }

        public Task CloseAsync()
        {
            logger.LogDebug("[{0}] Disconnecting WS connection", Id);
            cts.Cancel();
            return Task.CompletedTask;
        }

        public async Task SendAsync(byte[] messageBytes, CancellationToken cancellationToken, WebSocketMessageType messageType = WebSocketMessageType.Binary)
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                if (messageBytes.Length > _sendSegmentSize)
                {
                    int messageOffset = 0;
                    int messageBytesToSend = messageBytes.Length;

                    while (messageBytesToSend > 0)
                    {
                        int messageSegmentSize = Math.Min(_sendSegmentSize, messageBytesToSend);
                        ArraySegment<byte> messageSegment = new ArraySegment<byte>(messageBytes, messageOffset, messageSegmentSize);

                        messageOffset += messageSegmentSize;
                        messageBytesToSend -= messageSegmentSize;

                        await _webSocket.SendAsync(messageSegment, messageType, GetMessageFlags(messageBytesToSend == 0, !_useCompression), cancellationToken);
                    }
                }
                else
                {
                    ArraySegment<byte> messageSegment = new ArraySegment<byte>(messageBytes, 0, messageBytes.Length);

                    await _webSocket.SendAsync(messageSegment, messageType, GetMessageFlags(true, !_useCompression), cancellationToken);
                }
            }
        }

        public Task SendAsync(string message, CancellationToken cancellationToken)
        {
            return SendAsync(Encoding.UTF8.GetBytes(message), cancellationToken, WebSocketMessageType.Text);
        }

        public async Task ReceiveUntilCloseAsync()
        {
            if (_usePipelines)
            {
                logger.LogDebug("[{0}] Using pipelines for WebSocket", Id);
                await ReceiveUsingPipelinesAsync();
            }
            else
            {
                logger.LogDebug("[{0}] Using buffers for WebSocket", Id);
                await ReceiveUsingBuffersAsync();
            }

            await CloseAsync();
        }

        private async Task ReceiveUsingPipelinesAsync()
        {
            var options = new PipeOptions(useSynchronizationContext: false);
            var pipe = new Pipe(options);
            Task writing = FillPipeAsync(_webSocket, pipe.Writer);
            Task reading = ProcessPipeAsync(pipe.Reader);

            await Task.WhenAll(reading, writing);

            pipe.Reset();
        }

        private async Task FillPipeAsync(WebSocket webSocket, PipeWriter writer)
        {
            while (_webSocket.State == WebSocketState.Open && !cts.IsCancellationRequested)
            {
                try
                {
                    var message = await webSocket.ReceiveAsync(writer.GetMemory(_receiveBufferSize), cts.Token);

                    while (!cts.IsCancellationRequested && !message.EndOfMessage && message.MessageType != WebSocketMessageType.Close)
                    {
                        if (message.Count > 0)
                        {
                            writer.Advance(message.Count);
                            message = await webSocket.ReceiveAsync(writer.GetMemory(_receiveBufferSize), cts.Token);
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

                    FlushResult result = await writer.FlushAsync();

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
                    break;
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
                {
                    logger.LogError(ex, "[{0}] Error occurred during receiving from WebSocket", Id);
                    break;
                }
            }

            CloseStatus = webSocket.CloseStatus;
            CloseStatusDescription = webSocket.CloseStatusDescription;

            await writer.CompleteAsync();
        }

        private async Task ProcessPipeAsync(PipeReader reader)
        {
            while (_webSocket.State == WebSocketState.Open && !cts.IsCancellationRequested)
            {
                try
                {
                    ReadResult result = await reader.ReadAsync();
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    try
                    {
                        await OnReceiveTextAsync(Encoding.UTF8.GetString(buffer));
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "[{0}] There was a problem handling the WebSocket message", Id);
                    }

                    reader.AdvanceTo(buffer.End, buffer.End);

                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
                {
                    logger.LogError(ex, "[{0}] Error occurred during processing from WebSocket", Id);
                    break;
                }
            }

            await reader.CompleteAsync();
        }

        private async Task ReceiveUsingBuffersAsync()
        {
            try
            {
                byte[] receivePayloadBuffer = new byte[_receiveBufferSize];
                WebSocketReceiveResult webSocketReceiveResult = await _webSocket.ReceiveAsync(new ArraySegment<byte>(receivePayloadBuffer), cts.Token);
                while (webSocketReceiveResult.MessageType != WebSocketMessageType.Close && !cts.IsCancellationRequested)
                {
                    byte[] webSocketMessage = await ReceiveMessagePayloadAsync(webSocketReceiveResult, receivePayloadBuffer, cts.Token);
                    // Only Text is supported
                    if (webSocketReceiveResult.MessageType == WebSocketMessageType.Text)
                    {
                        await OnReceiveTextAsync(Encoding.UTF8.GetString(webSocketMessage));
                    }

                    webSocketReceiveResult = await _webSocket.ReceiveAsync(new ArraySegment<byte>(receivePayloadBuffer), cts.Token);
                }

                CloseStatus = webSocketReceiveResult.CloseStatus.Value;
                CloseStatusDescription = webSocketReceiveResult.CloseStatusDescription;
            }
            catch (OperationCanceledException)
            {
                CloseStatus = WebSocketCloseStatus.NormalClosure;
                CloseStatusDescription = "WebSocket was requested to close";
            }
            catch (WebSocketException wsex) when (wsex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                logger.LogError(wsex, "[{0}] Error occurred during receiving from WebSocket", Id);
            }
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

        private async Task<byte[]> ReceiveMessagePayloadAsync(WebSocketReceiveResult webSocketReceiveResult, byte[] receivePayloadBuffer, CancellationToken cancellationToken)
        {
            byte[] messagePayload = null;

            if (webSocketReceiveResult.EndOfMessage)
            {
                messagePayload = new byte[webSocketReceiveResult.Count];
                Array.Copy(receivePayloadBuffer, messagePayload, webSocketReceiveResult.Count);
            }
            else
            {
                using (MemoryStream messagePayloadStream = new MemoryStream())
                {
                    messagePayloadStream.Write(receivePayloadBuffer, 0, webSocketReceiveResult.Count);
                    while (!webSocketReceiveResult.EndOfMessage)
                    {
                        webSocketReceiveResult = await _webSocket.ReceiveAsync(new ArraySegment<byte>(receivePayloadBuffer), cancellationToken);
                        messagePayloadStream.Write(receivePayloadBuffer, 0, webSocketReceiveResult.Count);
                    }

                    messagePayload = messagePayloadStream.ToArray();
                }
            }

            return messagePayload;
        }

        private async Task OnReceiveTextAsync(string webSocketMessage)
        {
            if (ReceiveTextAsync != null)
            {
                await ReceiveTextAsync(webSocketMessage);
            }
        }
    }
}
