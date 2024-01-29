using System;
using System.Threading.Tasks;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;
using GLOKON.GuacWS.Server.Infrastructure;
using GLOKON.GuacWS.Server.Services;
using GLOKON.GuacWS.Server.Guac;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using GLOKON.GuacWS.Server.Infrastructure.Token;
using System.Text.Json;
using System.Security.Claims;

namespace GLOKON.GuacWS.Server.Middlewares
{
    internal class WebSocketConnectionsMiddleware
    {
        private readonly WebSocketConnectionsOptions options;
        private readonly RequestDelegate next;
        private readonly TokenAuthenticationOptions tokenOptions;
        private readonly GlobalStore store;
        private readonly ILogger logger;
        private readonly GuacOptions guacOptions;
        private readonly IGuacConnectionsService connectionsService;

        public WebSocketConnectionsMiddleware(
            RequestDelegate next,
            ILoggerFactory loggerFactory,
            IOptionsMonitor<TokenAuthenticationOptions> tokenOptions,
            IOptions<WebSocketConnectionsOptions> options,
            IOptions<GuacOptions> guacOptions,
            GlobalStore store,
            IGuacConnectionsService connectionsService)
        {
            logger = loggerFactory.CreateLogger("GuacWS"); // Create default logger
            this.options = options.Value;
            this.guacOptions = guacOptions.Value;
            this.next = next;
            this.tokenOptions = tokenOptions.CurrentValue;
            this.store = store;
            this.connectionsService = connectionsService ?? throw new ArgumentNullException(nameof(connectionsService));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                if (ValidateOrigin(context) && (context.User.Identity?.IsAuthenticated ?? false))
                {
                    ConnectionProfile profile = JsonSerializer.Deserialize<ConnectionProfile>(context.User.FindFirstValue(tokenOptions.TokenClaimName), tokenOptions.TokenSerializerOptions);
                    Guid connectionId = Guid.NewGuid();

                    logger.LogDebug("[{id}] Received a new WS request", connectionId);

                    WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
                    {
                        SubProtocol = "guacamole",
                        DangerousEnableCompression = options.UseCompression
                    });

                    logger.LogDebug("[{id}] Accepted a new WS connection", connectionId);

                    using (WebSocketConnection webSocketConnection = new(connectionId, webSocket, options, logger))
                    using (GuacDClient guacDClient = new(connectionId, guacOptions.GuacD, logger))
                    {
                        GuacConnection guacConnection = new(connectionId, webSocketConnection, guacDClient, guacOptions, store, logger);
                        connectionsService.AddConnection(connectionId, guacConnection);

                        try
                        {
                            await guacConnection.StartAsync(profile);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "[{id}] There was a problem starting the GuacD connection", connectionId);
                        }

                        try
                        {
                            await guacConnection.StopAsync();
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "[{id}] There was a problem stopping the GuacD connection", connectionId);
                        }

                        logger.LogInformation("[{id}] Ending GuacWS session", connectionId);
                        connectionsService.RemoveConnection(connectionId);
                    }
                }
                else
                {
                    logger.LogDebug("Blocked WS request from invalid origin: {origin}", context.Request.Headers.Origin);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                }
            }
            else
            {
                await next.Invoke(context);
            }
        }

        private bool ValidateOrigin(HttpContext context)
        {
            return (options.AllowedOrigins == null) || (options.AllowedOrigins.Count == 0) || (options.AllowedOrigins.Contains(context.Request.Headers.Origin.ToString()));
        }
    }
}
