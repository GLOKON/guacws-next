using System.Collections.Generic;
using GLOKON.GuacWS.Server.Infrastructure;

namespace GLOKON.GuacWS.Server.Middlewares
{
    internal class WebSocketConnectionsOptions
    {
        public HashSet<string> AllowedOrigins { get; set; }

        public int? SendSegmentSize { get; set; }

        public int ReceivePayloadBufferSize { get; set; }

        public WebSocketConnectionsOptions()
        {
            ReceivePayloadBufferSize = 4 * 1024;
        }
    }
}
