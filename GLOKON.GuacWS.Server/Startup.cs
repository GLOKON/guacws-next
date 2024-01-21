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
            services.Configure<WebSocketConnectionsOptions>(Configuration.GetRequiredSection(nameof(WebSocketConnectionsOptions)));
            services.Configure<GuacOptions>(Configuration.GetRequiredSection(nameof(GuacOptions)));

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
            services.AddSingleton<SymmetricCipher>((services) =>
            {
                var options = services.GetRequiredService<IOptions<GuacOptions>>().Value;

                switch (options.Cipher.Type)
                {
                    case CipherType.AES:
                        return new SymmetricCipher(Aes.Create(), options.Cipher.Key, options.Cipher.Mode, options.Cipher.KeySize);
                    case CipherType.DES:
                        return new SymmetricCipher(DES.Create(), options.Cipher.Key, options.Cipher.Mode, options.Cipher.KeySize);
                    case CipherType.RC2:
                        return new SymmetricCipher(RC2.Create(), options.Cipher.Key, options.Cipher.Mode, options.Cipher.KeySize);
                    case CipherType.Rijndael:
                        return new SymmetricCipher(Rijndael.Create(), options.Cipher.Key, options.Cipher.Mode, options.Cipher.KeySize);
                    case CipherType.TripleDES:
                        return new SymmetricCipher(TripleDES.Create(), options.Cipher.Key, options.Cipher.Mode, options.Cipher.KeySize);
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

            app.UseDefaultFiles()
                .UseStaticFiles()
                .UseWebSockets()
                .MapWebSocketConnections("/ws");
        }
    }
}
