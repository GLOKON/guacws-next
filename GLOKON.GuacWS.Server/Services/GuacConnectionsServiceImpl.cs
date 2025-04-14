using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GLOKON.GuacWS.Server.Guac;

namespace GLOKON.GuacWS.Server.Services
{
    internal class GuacConnectionsServiceImpl : IGuacConnectionsService
    {
        private readonly ConcurrentDictionary<Guid, IGuacConnection> _connections = new();

        public List<IGuacConnection> GetConnectionsByGroup(string groupName)
        {
            return _connections.Values.Where(connection => connection.ConnectionProfile?.Group == groupName).ToList();
        }

        public bool TryGetConnection(Guid id, out IGuacConnection connection)
        {
            return _connections.TryGetValue(id, out connection);
        }

        public void AddConnection(Guid id, IGuacConnection connection)
        {
            _connections.TryAdd(id, connection);
        }

        public void RemoveConnection(Guid id)
        {
            _connections.TryRemove(id, out _);
        }
    }
}
