using GLOKON.GuacWS.Server.Infrastructure;
using GLOKON.GuacWS.Server.Logger;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Net;

namespace GLOKON.GuacWS.Server
{
    public class Program
    {
        private static ILogger<Program> logger;

        private static Queue<string> queuedLogMessages = new Queue<string>();

        public static void Main(string[] args)
        {
            var  builder = WebHost.CreateDefaultBuilder<Startup>(args)
                .SuppressStatusMessages(true)
                .ConfigureKestrel((context, kestrelOptions) =>
                {
                    kestrelOptions.AddServerHeader = false;

                    var serverOptionsVal = kestrelOptions.ApplicationServices.GetRequiredService<IOptions<ServerOptions>>();

                    if (serverOptionsVal.Value is ServerOptions serverOptions)
                    {
                        if (!string.IsNullOrEmpty(serverOptions.ListenOn))
                        {
                            IPAddress listenAddress = IPAddress.Parse(serverOptions.ListenOn);
                            kestrelOptions.Listen(listenAddress, serverOptions.HttpPort);
                            LogMessage(string.Format("Listening (HTTP): http://{0}:{1}", listenAddress.ToString(), serverOptions.HttpPort));

                            if (serverOptions.LetsEncrypt.IsEnabled())
                            {
                                kestrelOptions.Listen(listenAddress, serverOptions.HttpsPort, listenOptions =>
                                {
                                    listenOptions.UseHttps(httpsOptions =>
                                    {
                                        httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                                        httpsOptions.UseLettuceEncrypt(kestrelOptions.ApplicationServices);
                                    });
                                });

                                LogMessage(string.Format("Listening (LetsEncrypt): https://{0}:{1}", listenAddress.ToString(), serverOptions.HttpsPort));
                            }
                            else if (serverOptions.SSL.IsEnabled())
                            {
                                kestrelOptions.Listen(listenAddress, serverOptions.HttpsPort, listenOptions =>
                                {
                                    listenOptions.UseHttps(serverOptions.SSL.CertificatePath, serverOptions.SSL.CertificatePassword);
                                });

                                LogMessage(string.Format("Listening (SSL): https://{0}:{1}", listenAddress.ToString(), serverOptions.HttpsPort));
                            }
                            else if (context.HostingEnvironment.IsDevelopment())
                            {
                                // Use development certificate
                                kestrelOptions.Listen(listenAddress, serverOptions.HttpsPort, listenOptions =>
                                {
                                    listenOptions.UseHttps();
                                });

                                LogMessage(string.Format("Listening (DevSSL): https://{0}:{1}", listenAddress.ToString(), serverOptions.HttpsPort));
                            }
                        }

                        if (!string.IsNullOrEmpty(serverOptions.ListenOnSocket))
                        {
                            kestrelOptions.ListenUnixSocket(serverOptions.ListenOnSocket);
                            LogMessage(string.Format("Listening (Unix Socket): {0}", serverOptions.ListenOnSocket));
                        }
                    }
                })
                .UseUrls();

            builder.ConfigureLogging((logBuilder) =>
            {
                logBuilder.AddConsoleFormatter<WebConsoleFormatter, SimpleConsoleFormatterOptions>((options) =>
                {
                    options.SingleLine = true;
                    options.IncludeScopes = false;
                    options.ColorBehavior = LoggerColorBehavior.Default;
                    options.TimestampFormat = "dd/MM/yyyy HH:mm:ss ";
                });
                logBuilder.AddConsole((options) =>
                {
                    options.FormatterName = "webconsole";
                });
            });

            var app = builder.Build();
            logger = app.Services.GetRequiredService<ILogger<Program>>();
            LogMessage("GuacWS Server is now running");

            while (queuedLogMessages.TryDequeue(out var message))
            {
                LogMessage(message);
            }
            app.Run();
        }

        private static void LogMessage(string message)
        {
            if (logger == null)
            {
                queuedLogMessages.Enqueue(message);
            }
            else
            {
                logger.LogInformation(message);
            }
        }
    }
}
