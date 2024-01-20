namespace GLOKON.GuacWS.Server.Infrastructure
{
    internal class PlainTextWebSocketSubprotocol : TextWebSocketSubprotocolBase, ITextWebSocketSubprotocol
    {
        public string SubProtocol => "aspnetcore-ws.plaintext";
    }
}
