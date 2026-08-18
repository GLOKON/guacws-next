using System;
using System.Collections.Generic;
using GLOKON.GuacWS.Server.Guac;

namespace GLOKON.GuacWS.Server.Services
{
    public interface IGuacConnectionsService
    {
        List<IGuacConnection> GetConnectionsByGroup(string groupName);

        void AddConnection(Guid id, IGuacConnection connection);

        bool TryGetConnection(Guid id, out IGuacConnection connection);

        void RemoveConnection(Guid id);
    }
}
