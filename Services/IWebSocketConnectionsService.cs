using System;
using System.Threading;
using System.Threading.Tasks;
using GLOKON.GuacWS.Server.Infrastructure;

namespace GLOKON.GuacWS.Server.Services
{
    internal interface IWebSocketConnectionsService
    {
        void AddConnection(WebSocketConnection connection);

        void RemoveConnection(Guid connectionId);
    }
}
