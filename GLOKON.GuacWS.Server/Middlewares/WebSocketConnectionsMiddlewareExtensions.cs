using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;

namespace GLOKON.GuacWS.Server.Middlewares
{
    internal static class WebSocketConnectionsMiddlewareExtensions
    {
        public static IApplicationBuilder MapWebSocketConnections(this IApplicationBuilder app, PathString pathMatch)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }

            return app.Map(pathMatch, branchedApp => branchedApp.UseMiddleware<WebSocketConnectionsMiddleware>());
        }
    }
}
