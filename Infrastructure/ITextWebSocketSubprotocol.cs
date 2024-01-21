using System;
using System.Threading;
using System.Threading.Tasks;

namespace GLOKON.GuacWS.Server.Infrastructure
{
    internal interface ITextWebSocketSubprotocol
    {
        string SubProtocol { get; }

        Task SendAsync(string message, Func<byte[], CancellationToken, Task> sendMessageBytesAsync, CancellationToken cancellationToken);

        string Read(string rawMessage);
    }
}
