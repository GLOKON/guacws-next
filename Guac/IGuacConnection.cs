using GLOKON.GuacWS.Server.Infrastructure.Token;
using System;
using System.Threading.Tasks;

namespace GLOKON.GuacWS.Server.Guac
{
    public interface IGuacConnection
    {
        Guid Id { get; }

        ConnectionProfile ConnectionProfile { get; }

        string UserDrive { get; }

        Task StartAsync(ConnectionProfile profile);

        Task StopAsync();
    }
}
