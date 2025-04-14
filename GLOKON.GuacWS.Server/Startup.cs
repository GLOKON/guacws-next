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
using GLOKON.GuacWS.Server.Guac;
using GLOKON.GuacWS.Server.Infrastructure.Token;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog.Events;
using Serilog;

namespace GLOKON.GuacWS.Server
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(Configuration)
                .MinimumLevel.Override("Microsoft.AspNetCore.Mvc.Internal", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore.DataProtection", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore.Authentication", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore.Hosting", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore.Mvc", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore.Routing", LogEventLevel.Warning)
                .WriteTo.Console()
                .CreateLogger();
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSerilog();

            var serverSection = Configuration.GetRequiredSection("Server");
            ServerOptions serverOptions = serverSection.Get<ServerOptions>();
            services.Configure<ServerOptions>(serverSection);
            services.Configure<CipherOptions>(Configuration.GetRequiredSection("Cipher"));
            services.Configure<WebSocketConnectionsOptions>(Configuration.GetRequiredSection("WebSocket"));
            services.Configure<GuacOptions>(Configuration.GetRequiredSection("Guac"));

            services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyOrigin()
                    .AllowAnyHeader()
                    .AllowAnyMethod();
                });
            });

            services.AddSingleton<GlobalStore>();
            services.AddSingleton((services) =>
            {
                var options = services.GetRequiredService<IOptions<CipherOptions>>().Value;

                return options.Type switch
                {
                    CipherType.AES => new SymmetricCipher(Aes.Create(), options.Key, options.Mode, options.KeySize),
                    CipherType.DES => new SymmetricCipher(DES.Create(), options.Key, options.Mode, options.KeySize),
                    CipherType.RC2 => new SymmetricCipher(RC2.Create(), options.Key, options.Mode, options.KeySize),
                    CipherType.Rijndael => new SymmetricCipher(Rijndael.Create(), options.Key, options.Mode, options.KeySize),
                    CipherType.TripleDES => new SymmetricCipher(TripleDES.Create(), options.Key, options.Mode, options.KeySize),
                    _ => null,
                };
            });
            services.AddSingleton<IGuacConnectionsService, GuacConnectionsServiceImpl>();
            services.AddHostedService<TimestampPingService>();

            if (serverOptions.LetsEncrypt.IsEnabled())
            {
                services.AddLettuceEncrypt(options =>
                {
                    options.AcceptTermsOfService = true;
                    options.DomainNames = [.. serverOptions.LetsEncrypt.Domains];
                    options.EmailAddress = serverOptions.LetsEncrypt.EmailAddress;
                    options.UseStagingServer = serverOptions.LetsEncrypt.UseStagingServer;
                });
            }

            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

                foreach (var trustedProxy in serverOptions.TrustedProxies)
                {
                    if (IPNetwork.TryParse(trustedProxy, out var trustedNetwork) && trustedNetwork != null)
                    {
                        options.KnownNetworks.Add(trustedNetwork);
                    }
                }
            });
            services.AddDataProtection()
                .UseCryptographicAlgorithms(new AuthenticatedEncryptorConfiguration
                {
                    EncryptionAlgorithm = EncryptionAlgorithm.AES_256_CBC,
                    ValidationAlgorithm = ValidationAlgorithm.HMACSHA256
                });
            services.AddControllers();
            services.AddAuthentication(TokenAuthenticationOptions.Scheme)
                .AddScheme<TokenAuthenticationOptions, TokenAuthenticationHandler>(TokenAuthenticationOptions.Scheme, null);
            services.AddAuthorization();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IOptions<ServerOptions> serverOptionsVal)
        {
            app.UseForwardedHeaders();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/error");
            }

            if (serverOptionsVal.Value is ServerOptions serverOptions && serverOptions.IsUsingHttps())
            {
                if (serverOptions.UseHsts)
                {
                    app.UseHsts();
                }

                app.UseHttpsRedirection();
            }

            app.UseDefaultFiles()
                .UseWebSockets()
                .UseRouting()
                .UseCors()
                .UseAuthentication()
                .UseAuthorization()
                .UseWebSocketConnectionMiddleware()
                .UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                    endpoints.MapFallbackToFile("/index.html");
                })
                .UseStaticFiles();
        }
    }
}
