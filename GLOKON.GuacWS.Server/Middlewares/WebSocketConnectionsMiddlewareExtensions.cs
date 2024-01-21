using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;

namespace GLOKON.GuacWS.Server.Middlewares
{
    internal static class WebSocketConnectionsMiddlewareExtensions
    {
        public static IApplicationBuilder UseWebSocketConnectionMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<WebSocketConnectionsMiddleware>();
        }
    }
}
