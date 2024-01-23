using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using GLOKON.GuacWS.Server.Infrastructure;

namespace GLOKON.GuacWS.Server.Services
{
    internal class WebSocketConnectionsService : IWebSocketConnectionsService
    {
        private readonly ConcurrentDictionary<Guid, WebSocketConnection> _connections = new ConcurrentDictionary<Guid, WebSocketConnection>();

        public void AddConnection(WebSocketConnection connection)
        {
            _connections.TryAdd(connection.Id, connection);
        }

        public void RemoveConnection(Guid connectionId)
        {
            WebSocketConnection connection;

            _connections.TryRemove(connectionId, out connection);
        }
    }
}
