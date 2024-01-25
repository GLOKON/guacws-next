using System;
using System.Collections.Concurrent;
using GLOKON.GuacWS.Server.Infrastructure;

namespace GLOKON.GuacWS.Server.Services
{
    internal class WebSocketConnectionsService : IWebSocketConnectionsService
    {
        private readonly ConcurrentDictionary<Guid, WebSocketConnection> _connections = new();

        public void AddConnection(Guid id, WebSocketConnection connection)
        {
            _connections.TryAdd(id, connection);
        }

        public void RemoveConnection(Guid id)
        {
            _connections.TryRemove(id, out _);
        }
    }
}
