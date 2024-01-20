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

namespace GLOKON.GuacWS.Server.Middlewares
{
    internal class WebSocketConnectionsMiddleware
    {
        #region Fields
        private readonly WebSocketConnectionsOptions options;
        private readonly WebSocketConnectionsProtocols protocols;
        private readonly GuacOptions guacOptions;
        private readonly IWebSocketConnectionsService connectionsService;
        #endregion

        #region Constructor
        public WebSocketConnectionsMiddleware(RequestDelegate next, IOptions<WebSocketConnectionsOptions> options, IOptions<GuacOptions> guacOptions, WebSocketConnectionsProtocols protocols, IWebSocketConnectionsService connectionsService)
        {
            this.options = options.Value;
            this.guacOptions = guacOptions.Value;
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

                    using (WebSocketConnection webSocketConnection = new(webSocket, textSubProtocol ?? protocols.DefaultSubProtocol, options.SendSegmentSize, options.ReceivePayloadBufferSize))
                    using (GuacDClient guacDClient = new(guacOptions.GuacD))
                    {
                        connectionsService.AddConnection(webSocketConnection);
                        GuacConnection guacConnection = new(webSocketConnection, guacDClient, guacOptions);

                        try
                        {
                            await guacConnection.StartAsync(context);
                            await webSocketConnection.ReceiveMessagesUntilCloseAsync();
                        }
                        catch (Exception ex)
                        {
                            // TODO: Log error
                        }

                        if (webSocketConnection.CloseStatus.HasValue)
                        {
                            await webSocket.CloseAsync(webSocketConnection.CloseStatus.Value, webSocketConnection.CloseStatusDescription, CancellationToken.None);
                        }

                        await guacConnection.StopAsync();
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
