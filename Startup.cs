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
            var serverSection = Configuration.GetRequiredSection("Server");
            ServerOptions serverOptions = serverSection.Get<ServerOptions>();
            services.Configure<ServerOptions>(serverSection);
            services.Configure<CipherOptions>(Configuration.GetRequiredSection("Cipher"));
            services.Configure<WebSocketConnectionsOptions>(Configuration.GetRequiredSection("WebSocket"));
            services.Configure<GuacOptions>(Configuration.GetRequiredSection("Guac"));

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

            if (serverOptions.LetsEncrypt.IsEnabled())
            {
                services.AddLettuceEncrypt(options =>
                {
                    options.AcceptTermsOfService = true;
                    options.DomainNames = serverOptions.LetsEncrypt.Domains.ToArray();
                    options.EmailAddress = serverOptions.LetsEncrypt.EmailAddress;
                    options.UseStagingServer = serverOptions.LetsEncrypt.UseStagingServer;
                });
            }
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IOptions<LetsEncryptOptions> letsEncrypt)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/error");
            }

            if (letsEncrypt.Value is LetsEncryptOptions options && options.IsEnabled())
            {
                if (options.UseHsts)
                {
                    app.UseHsts();
                }

                app.UseHttpsRedirection();
            }

            app.UseWebSockets()
                .UseWebSocketConnectionMiddleware()
                .UseDefaultFiles()
                .UseStaticFiles();
        }
    }
}
