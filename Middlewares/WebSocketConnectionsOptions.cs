using System.Collections.Generic;
using GLOKON.GuacWS.Server.Infrastructure;

namespace GLOKON.GuacWS.Server.Middlewares
{
    internal class WebSocketConnectionsOptions
    {
        public HashSet<string> AllowedOrigins { get; set; } = new HashSet<string>();

        public bool UseCompression { get; set; } = true;

        public int ReceiveBufferSize { get; set; } = 8192;

        public int CloseTimeout { get; set; } = 1500;
    }
}
