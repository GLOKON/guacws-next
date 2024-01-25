using System;
using GLOKON.GuacWS.Server.Infrastructure;

namespace GLOKON.GuacWS.Server.Services
{
    internal interface IWebSocketConnectionsService
    {
        void AddConnection(Guid id, WebSocketConnection connection);

        void RemoveConnection(Guid id);
    }
}
