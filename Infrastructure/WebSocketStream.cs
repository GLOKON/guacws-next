using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading.Tasks;
using System.Threading;

namespace GLOKON.GuacWS.Server.Infrastructure
{
    internal class WebSocketStream : Stream
    {
        private readonly WebSocket webSocket;
        private readonly WebSocketMessageType messageType;
        private readonly bool useCompression;

        public WebSocketStream(WebSocket webSocket, WebSocketMessageType messageType, bool useCompression)
        {
            this.webSocket = webSocket;
            this.messageType = messageType;
            this.useCompression = useCompression;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => webSocket.SendAsync(buffer, messageType, GetMessageFlags(true, !useCompression), cancellationToken);

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override bool CanRead => throw new NotImplementedException();
        public override bool CanSeek => throw new NotImplementedException();
        public override bool CanWrite => throw new NotImplementedException();
        public override long Length => throw new NotImplementedException();
        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public override void Flush() => throw new NotImplementedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();
        public override void SetLength(long value) => throw new NotImplementedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            webSocket.SendAsync(buffer, messageType, GetMessageFlags(true, !useCompression), CancellationToken.None);

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
