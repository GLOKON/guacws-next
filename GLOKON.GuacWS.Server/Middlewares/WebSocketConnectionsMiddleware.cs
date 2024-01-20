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

namespace GLOKON.GuacWS.Server.Middlewares
{
    internal class WebSocketConnectionsMiddleware
    {
        #region Fields
        private readonly WebSocketConnectionsOptions options;
        private readonly ILogger<WebSocketConnectionsMiddleware> webSocketMWLogger;
        private readonly ILogger<WebSocketConnection> webSocketConnLogger;
        private readonly ILogger<GuacDClient> guacDClientLogger;
        private readonly ILogger<GuacConnection> guacConnLogger;
        private readonly WebSocketConnectionsProtocols protocols;
        private readonly GuacOptions guacOptions;
        private readonly IWebSocketConnectionsService connectionsService;
        #endregion

        #region Constructor
        public WebSocketConnectionsMiddleware(
            RequestDelegate next,
            IOptions<WebSocketConnectionsOptions> options,
            IOptions<GuacOptions> guacOptions,
            ILogger<WebSocketConnectionsMiddleware> webSocketMWLogger,
            ILogger<WebSocketConnection> webSocketConnLogger,
            ILogger<GuacDClient> guacDClientLogger,
            ILogger<GuacConnection> guacConnLogger,
            WebSocketConnectionsProtocols protocols,
            IWebSocketConnectionsService connectionsService)
        {
            this.options = options.Value;
            this.guacOptions = guacOptions.Value;
            this.webSocketMWLogger = webSocketMWLogger;
            this.webSocketConnLogger = webSocketConnLogger;
            this.guacDClientLogger = guacDClientLogger;
            this.guacConnLogger = guacConnLogger;
            this.protocols = protocols;
            this.connectionsService = connectionsService ?? throw new ArgumentNullException(nameof(connectionsService));
        }
        #endregion

        #region Methods
        public async Task Invoke(HttpContext context)
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                if (ValidateOrigin(context))
                {
                    ITextWebSocketSubprotocol textSubProtocol = NegotiateSubProtocol(context.WebSockets.WebSocketRequestedProtocols);

                    WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
                    {
                        SubProtocol = textSubProtocol?.SubProtocol,
                        DangerousEnableCompression = true
                    });

                    Guid connectionId = Guid.NewGuid();
                    using (WebSocketConnection webSocketConnection = new(connectionId, webSocket, textSubProtocol ?? protocols.DefaultSubProtocol, options.SendSegmentSize, options.ReceivePayloadBufferSize, webSocketConnLogger))
                    using (GuacDClient guacDClient = new(connectionId, guacOptions.GuacD, guacDClientLogger))
                    {
                        connectionsService.AddConnection(webSocketConnection);
                        GuacConnection guacConnection = new(webSocketConnection, guacDClient, guacOptions, guacConnLogger);

                        try
                        {
                            await guacConnection.StartAsync(context);
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
                            await webSocket.CloseAsync(webSocketConnection.CloseStatus.Value, webSocketConnection.CloseStatusDescription, CancellationToken.None);
                        }
                        else
                        {
                            await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "There was a problem starting the GuacD connection", CancellationToken.None);
                        }

                        connectionsService.RemoveConnection(webSocketConnection.Id);
                    }
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                }
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
        }

        private bool ValidateOrigin(HttpContext context)
        {
            return (options.AllowedOrigins == null) || (options.AllowedOrigins.Count == 0) || (options.AllowedOrigins.Contains(context.Request.Headers["Origin"].ToString()));
        }

        private ITextWebSocketSubprotocol NegotiateSubProtocol(IList<string> requestedSubProtocols)
        {
            ITextWebSocketSubprotocol subProtocol = null;

            foreach (ITextWebSocketSubprotocol supportedSubProtocol in protocols.SupportedSubProtocols)
            {
                if (requestedSubProtocols.Contains(supportedSubProtocol.SubProtocol))
                {
                    subProtocol = supportedSubProtocol;
                    break;
                }
            }

            return subProtocol;
        }
        #endregion
    }
}
