using GLOKON.GuacWS.Server.Infrastructure;
using System.Collections.Generic;

namespace GLOKON.GuacWS.Server.Middlewares
{
    internal class WebSocketConnectionsProtocols
    {
        public IList<ITextWebSocketSubprotocol> SupportedSubProtocols { get; set; }

        public ITextWebSocketSubprotocol DefaultSubProtocol { get; set; }
    }
}
