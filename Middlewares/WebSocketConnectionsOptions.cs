using System.Collections.Generic;
using GLOKON.GuacWS.Server.Infrastructure;

namespace GLOKON.GuacWS.Server.Middlewares
{
    internal class WebSocketConnectionsOptions
    {
        public HashSet<string> AllowedOrigins { get; set; } = new HashSet<string>();

        public bool UseCompression { get; set; } = false;

        public bool UsePipelines { get; set; } = true;

        public int SendSegmentSize { get; set; } = 4 * 1024;

        public int ReceivePayloadBufferSize { get; set; } = 4 * 1024;
    }
}
