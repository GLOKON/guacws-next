using Microsoft.Extensions.DependencyInjection;

namespace GLOKON.GuacWS.Server.Services
{
    internal static class WebSocketConnectionsServiceExtensions
    {
        public static IServiceCollection AddWebSocketConnections(this IServiceCollection services)
        {
            services.AddSingleton<WebSocketConnectionsService>();
            services.AddSingleton<IWebSocketConnectionsService>(serviceProvider => serviceProvider.GetService<WebSocketConnectionsService>());

            return services;
        }
    }
}
