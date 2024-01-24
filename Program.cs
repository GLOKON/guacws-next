using GLOKON.GuacWS.Server.Logger;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace GLOKON.GuacWS.Server
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var  builder = WebHost.CreateDefaultBuilder<Startup>(args)
                .SuppressStatusMessages(true)
                .ConfigureKestrel(options => options.AddServerHeader = false)
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
            var logger = app.Services.GetService<ILogger<Program>>();
            logger.LogInformation("GuacWS Server is now running");
            app.Run();
        }
    }
}
