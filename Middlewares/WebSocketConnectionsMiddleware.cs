using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using GLOKON.GuacWS.Server.Infrastructure;
using GLOKON.GuacWS.Server.Services;
using GLOKON.GuacWS.Server.Guac;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using GLOKON.GuacWS.Server.Cipher;

namespace GLOKON.GuacWS.Server.Middlewares
{
    internal class WebSocketConnectionsMiddleware
    {
        private readonly WebSocketConnectionsOptions options;
        private readonly RequestDelegate next;
        private readonly SymmetricCipher cipher;
        private readonly ILogger<WebSocketConnectionsMiddleware> webSocketMWLogger;
        private readonly ILogger<WebSocketConnection> webSocketConnLogger;
        private readonly ILogger<GuacDClient> guacDClientLogger;
        private readonly ILogger<GuacConnection> guacConnLogger;
        private readonly GuacOptions guacOptions;
        private readonly IWebSocketConnectionsService connectionsService;

        public WebSocketConnectionsMiddleware(
            RequestDelegate next,
            IOptions<WebSocketConnectionsOptions> options,
            IOptions<GuacOptions> guacOptions,
            SymmetricCipher cipher,
            ILogger<WebSocketConnectionsMiddleware> webSocketMWLogger,
            ILogger<WebSocketConnection> webSocketConnLogger,
            ILogger<GuacDClient> guacDClientLogger,
            ILogger<GuacConnection> guacConnLogger,
            IWebSocketConnectionsService connectionsService)
        {
            this.options = options.Value;
            this.guacOptions = guacOptions.Value;
            this.next = next;
            this.cipher = cipher;
            this.webSocketMWLogger = webSocketMWLogger;
            this.webSocketConnLogger = webSocketConnLogger;
            this.guacDClientLogger = guacDClientLogger;
            this.guacConnLogger = guacConnLogger;
            this.connectionsService = connectionsService ?? throw new ArgumentNullException(nameof(connectionsService));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                if (ValidateOrigin(context))
                {
                    Guid connectionId = Guid.NewGuid();

                    webSocketMWLogger.LogDebug("[{0}] Received a new WS request", connectionId);

                    WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
                    {
                        SubProtocol = "guacamole",
                        DangerousEnableCompression = options.UseCompression
                    });

                    webSocketMWLogger.LogDebug("[{0}] Accepted a new WS connection", connectionId);

                    using (WebSocketConnection webSocketConnection = new(connectionId, webSocket, options.UseCompression, options.UsePipelines, options.SendSegmentSize, options.ReceivePayloadBufferSize, webSocketConnLogger))
                    using (GuacDClient guacDClient = new(connectionId, guacOptions.GuacD, guacDClientLogger))
                    {
                        webSocketMWLogger.LogInformation("[{0}] Starting a new GuacWS session", connectionId);
                        connectionsService.AddConnection(webSocketConnection);
                        GuacConnection guacConnection = new(webSocketConnection, guacDClient, guacOptions, cipher, guacConnLogger);

                        try
                        {
                            await guacConnection.StartAsync(context.Request.Query.ToImmutableDictionary());
                        }
                        catch (Exception ex)
                        {
                            webSocketMWLogger.LogError(ex, "[{0}] There was a problem starting the GuacD connection", connectionId);
                        }

                        try
                        {
                            await guacConnection.StopAsync();
                        }
                        catch (Exception ex)
                        {
                            webSocketMWLogger.LogError(ex, "[{0}] There was a problem stopping the GuacD connection", connectionId);
                        }

                        if (webSocketConnection.CloseStatus.HasValue)
                        {
                            await webSocket.CloseOutputAsync(webSocketConnection.CloseStatus.Value, webSocketConnection.CloseStatusDescription, CancellationToken.None);
                        }
                        else
                        {
                            await webSocket.CloseOutputAsync(WebSocketCloseStatus.InternalServerError, "There was a problem starting the GuacD connection", CancellationToken.None);
                        }

                        webSocketMWLogger.LogInformation("[{0}] Ending GuacWS session", connectionId);
                        connectionsService.RemoveConnection(webSocketConnection.Id);
                    }
                }
                else
                {
                    webSocketMWLogger.LogDebug("Blocked WS request from invalid origin: {0}", context.Request.Headers["Origin"]);
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
            return (options.AllowedOrigins == null) || (options.AllowedOrigins.Count == 0) || (options.AllowedOrigins.Contains(context.Request.Headers["Origin"].ToString()));
        }
    }
}
