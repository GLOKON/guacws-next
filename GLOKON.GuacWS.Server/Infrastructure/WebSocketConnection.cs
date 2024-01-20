using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;
using GLOKON.GuacWS.Server.Services;
using Microsoft.Extensions.Logging;

namespace GLOKON.GuacWS.Server.Infrastructure
{
    internal class WebSocketConnection: IDisposable
    {
        #region Fields
        private readonly int _receivePayloadBufferSize;
        private readonly ILogger<WebSocketConnection> logger;
        private readonly int? _sendSegmentSize;

        private readonly WebSocket _webSocket;
        private readonly ITextWebSocketSubprotocol _textSubProtocol;
        private readonly CancellationTokenSource _cancellationTokenSource;
        #endregion

        #region Properties
        public Guid Id { get; }

        public WebSocketCloseStatus? CloseStatus { get; private set; } = null;

        public string CloseStatusDescription { get; private set; } = null;
        #endregion

        #region Events
        public event EventHandler<string> ReceiveText;

        public event EventHandler<byte[]> ReceiveBinary;
        #endregion

        #region Constructor
        public WebSocketConnection(Guid Id, WebSocket webSocket, ITextWebSocketSubprotocol textSubProtocol, int? sendSegmentSize, int receivePayloadBufferSize, ILogger<WebSocketConnection> logger)
        {
            this.Id = Id;
            _webSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
            _textSubProtocol = textSubProtocol ?? throw new ArgumentNullException(nameof(textSubProtocol));
            _sendSegmentSize = sendSegmentSize;
            _receivePayloadBufferSize = receivePayloadBufferSize;
            this.logger = logger;
            _cancellationTokenSource = new CancellationTokenSource();
        }
        #endregion

        #region Methods
        public Task CloseAsync()
        {
            _cancellationTokenSource.Cancel();
            return Task.CompletedTask;
        }

        public Task SendAsync(string message, CancellationToken cancellationToken)
        {
            return _textSubProtocol.SendAsync(message, SendTextMessageBytesAsync, cancellationToken);
        }

        public Task SendAsync(byte[] message, CancellationToken cancellationToken)
        {
            return SendMessageBytesAsync(message, WebSocketMessageType.Binary, cancellationToken: cancellationToken);
        }

        public async Task ReceiveUntilCloseAsync()
        {
            try
            {
                var cancellationToken = _cancellationTokenSource.Token;
                byte[] receivePayloadBuffer = new byte[_receivePayloadBufferSize];
                WebSocketReceiveResult webSocketReceiveResult = await _webSocket.ReceiveAsync(new ArraySegment<byte>(receivePayloadBuffer), cancellationToken);
                while (webSocketReceiveResult.MessageType != WebSocketMessageType.Close && !cancellationToken.IsCancellationRequested)
                {
                    byte[] webSocketMessage = await ReceiveMessagePayloadAsync(webSocketReceiveResult, receivePayloadBuffer, cancellationToken);
                    if (webSocketReceiveResult.MessageType == WebSocketMessageType.Binary)
                    {
                        OnReceiveBinaryAsync(webSocketMessage);
                    }
                    else
                    {
                        OnReceiveTextAsync(Encoding.UTF8.GetString(webSocketMessage));
                    }

                    webSocketReceiveResult = await _webSocket.ReceiveAsync(new ArraySegment<byte>(receivePayloadBuffer), cancellationToken);
                }

                CloseStatus = webSocketReceiveResult.CloseStatus.Value;
                CloseStatusDescription = webSocketReceiveResult.CloseStatusDescription;
            }
            catch (OperationCanceledException ocex)
            {
                CloseStatus = WebSocketCloseStatus.NormalClosure;
                CloseStatusDescription = "WebSocket was requested to close";
            }
            catch (WebSocketException wsex) when (wsex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                logger.LogError(wsex, "[{0}] Error occurred during receiving from WebSocket", Id);
            }
        }

        private Task SendTextMessageBytesAsync(byte[] messageBytes, CancellationToken cancellationToken)
        {
            return SendMessageBytesAsync(messageBytes, WebSocketMessageType.Text, cancellationToken: cancellationToken);
        }

        private async Task SendMessageBytesAsync(byte[] messageBytes, WebSocketMessageType messageType, bool compressMessage = true, CancellationToken cancellationToken = default)
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                if (_sendSegmentSize.HasValue && (_sendSegmentSize.Value < messageBytes.Length))
                {
                    int messageOffset = 0;
                    int messageBytesToSend = messageBytes.Length;

                    while (messageBytesToSend > 0)
                    {
                        int messageSegmentSize = Math.Min(_sendSegmentSize.Value, messageBytesToSend);
                        ArraySegment<byte> messageSegment = new ArraySegment<byte>(messageBytes, messageOffset, messageSegmentSize);

                        messageOffset += messageSegmentSize;
                        messageBytesToSend -= messageSegmentSize;

                        await _webSocket.SendAsync(messageSegment, messageType, GetMessageFlags(messageBytesToSend == 0, !compressMessage), cancellationToken);
                    }
                }
                else
                {
                    ArraySegment<byte> messageSegment = new ArraySegment<byte>(messageBytes, 0, messageBytes.Length);

                    await _webSocket.SendAsync(messageSegment, messageType, GetMessageFlags(true, !compressMessage), cancellationToken);
                }
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
            string message = _textSubProtocol.Read(webSocketMessage);

            await ReceiveTextAsync(this, message);
        }

        private async Task OnReceiveBinaryAsync(byte[] webSocketMessage)
        {
            await ReceiveBinaryAsync(this, webSocketMessage);
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
        }
        #endregion
    }
}
