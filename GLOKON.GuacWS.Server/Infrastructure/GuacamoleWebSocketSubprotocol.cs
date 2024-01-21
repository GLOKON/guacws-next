namespace GLOKON.GuacWS.Server.Infrastructure
{
    internal class GuacamoleWebSocketSubprotocol : TextWebSocketSubprotocolBase, ITextWebSocketSubprotocol
    {
        public string SubProtocol => "guacamole";
    }
}
