using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using GLOKON.GuacWS.Server.Infrastructure;
using GLOKON.GuacWS.Server.Services;
using GLOKON.GuacWS.Server.Middlewares;
using Microsoft.Extensions.Configuration;
using GLOKON.GuacWS.Server.Cipher;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

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
            services.Configure<ConsoleLifetimeOptions>(opts => opts.SuppressStatusMessages = true);
            services.Configure<CipherOptions>(Configuration.GetRequiredSection("Cipher"));
            services.Configure<WebSocketConnectionsOptions>(Configuration.GetRequiredSection("WebSocket"));
            services.Configure<GuacOptions>(Configuration.GetRequiredSection("Guac"));

            ITextWebSocketSubprotocol textWebSocketSubprotocol = new PlainTextWebSocketSubprotocol();
            services.AddSingleton(new WebSocketConnectionsProtocols
            {
                SupportedSubProtocols = new List<ITextWebSocketSubprotocol>
                {
                    textWebSocketSubprotocol,
                    new GuacamoleWebSocketSubprotocol(),
                    new JsonWebSocketSubprotocol(),
                },
                DefaultSubProtocol = textWebSocketSubprotocol,
            });
            services.AddSingleton((services) =>
            {
                var options = services.GetRequiredService<IOptions<CipherOptions>>().Value;

                switch (options.Type)
                {
                    case CipherType.AES:
                        return new SymmetricCipher(Aes.Create(), options.Key, options.Mode, options.KeySize);
                    case CipherType.DES:
                        return new SymmetricCipher(DES.Create(), options.Key, options.Mode, options.KeySize);
                    case CipherType.RC2:
                        return new SymmetricCipher(RC2.Create(), options.Key, options.Mode, options.KeySize);
                    case CipherType.Rijndael:
                        return new SymmetricCipher(Rijndael.Create(), options.Key, options.Mode, options.KeySize);
                    case CipherType.TripleDES:
                        return new SymmetricCipher(TripleDES.Create(), options.Key, options.Mode, options.KeySize);
                }

                return null;
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

            app.UseWebSockets()
                .UseWebSocketConnectionMiddleware()
                .UseDefaultFiles()
                .UseStaticFiles();
        }
    }
}
