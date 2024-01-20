using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using GLOKON.GuacWS.Server.Infrastructure;
using GLOKON.GuacWS.Server.Services;
using GLOKON.GuacWS.Server.Middlewares;
using Microsoft.Extensions.Configuration;

namespace GLOKON.GuacWS.Server
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<WebSocketConnectionsOptions>(Configuration.GetRequiredSection(nameof(WebSocketConnectionsOptions)));
            services.Configure<GuacOptions>(Configuration.GetRequiredSection(nameof(GuacOptions)));

            ITextWebSocketSubprotocol textWebSocketSubprotocol = new PlainTextWebSocketSubprotocol();
            services.AddSingleton(new WebSocketConnectionsProtocols
            {
                SupportedSubProtocols = new List<ITextWebSocketSubprotocol>
                {
                    textWebSocketSubprotocol
                },
                DefaultSubProtocol = textWebSocketSubprotocol,
            });
            services.AddWebSocketConnections();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/error");

                // TODO: Figure out a way to make this conditional if Kestrel is configured with SSL
                //app.UseHsts();
            }

            // TODO: Figure out a way to make this conditional if Kestrel is configured with SSL
            //app.UseHttpsRedirection();

            app.UseDefaultFiles()
                .UseStaticFiles()
                .UseWebSockets()
                .MapWebSocketConnections("");
        }
    }
}
