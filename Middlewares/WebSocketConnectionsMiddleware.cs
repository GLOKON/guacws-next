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
        private readonly ILogger<WebSocketConnection> webSocketConnLogger;
        private readonly ILogger<GuacDClient> guacDClientLogger;
        private readonly ILogger<GuacConnection> connLogger;
        private readonly GuacOptions guacOptions;
        private readonly IGuacConnectionsService connectionsService;

        public WebSocketConnectionsMiddleware(
            RequestDelegate next,
            IOptionsMonitor<TokenAuthenticationOptions> tokenOptions,
            IOptions<WebSocketConnectionsOptions> options,
            IOptions<GuacOptions> guacOptions,
            GlobalStore store,
            ILogger<WebSocketConnection> webSocketConnLogger,
            ILogger<GuacDClient> guacDClientLogger,
            ILogger<GuacConnection> connLogger,
            IGuacConnectionsService connectionsService)
        {
            this.options = options.Value;
            this.guacOptions = guacOptions.Value;
            this.next = next;
            this.tokenOptions = tokenOptions.CurrentValue;
            this.store = store;
            this.webSocketConnLogger = webSocketConnLogger;
            this.guacDClientLogger = guacDClientLogger;
            this.connLogger = connLogger;
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

                    connLogger.LogDebug("[{id}] Received a new WS request", connectionId);

                    WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
                    {
                        SubProtocol = "guacamole",
                        DangerousEnableCompression = options.UseCompression
                    });

                    connLogger.LogDebug("[{id}] Accepted a new WS connection", connectionId);

                    using (WebSocketConnection webSocketConnection = new(connectionId, webSocket, options, webSocketConnLogger))
                    using (GuacDClient guacDClient = new(connectionId, guacOptions.GuacD, guacDClientLogger))
                    {
                        GuacConnection guacConnection = new(connectionId, webSocketConnection, guacDClient, guacOptions, store, connLogger);
                        connectionsService.AddConnection(connectionId, guacConnection);

                        try
                        {
                            await guacConnection.StartAsync(profile);
                        }
                        catch (Exception ex)
                        {
                            connLogger.LogError(ex, "[{id}] There was a problem starting the GuacD connection", connectionId);
                        }

                        try
                        {
                            await guacConnection.StopAsync();
                        }
                        catch (Exception ex)
                        {
                            connLogger.LogError(ex, "[{id}] There was a problem stopping the GuacD connection", connectionId);
                        }

                        connLogger.LogInformation("[{id}] Ending GuacWS session", connectionId);
                        connectionsService.RemoveConnection(connectionId);
                    }
                }
                else
                {
                    connLogger.LogDebug("Blocked WS request from invalid origin: {origin}", context.Request.Headers.Origin);
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
