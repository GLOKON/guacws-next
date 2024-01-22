using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GLOKON.GuacWS.Server
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var  app = CreateWebHostBuilder(args).Build();
            var logger = app.Services.GetService<ILogger<Program>>();
            logger.LogInformation("GuacWS Server is now running");
            app.Run();
        }
        
        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .SuppressStatusMessages(true)
                .UseUrls()
                .UseStartup<Startup>();
    }
}
