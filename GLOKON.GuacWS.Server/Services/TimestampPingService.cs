using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;
using System.Threading;
using System;
using GLOKON.GuacWS.Server.Guac;
using Microsoft.Extensions.Options;
using GLOKON.GuacWS.Server.Middlewares;

namespace GLOKON.GuacWS.Server.Services
{
    internal class TimestampPingService : BackgroundService
    {
        private readonly GlobalStore store;
        private readonly GuacOptions options;

        public TimestampPingService(GlobalStore store, IOptions<GuacOptions> options)
        {
            this.store = store;
            this.options = options.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                store.UpdatePing(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                await Task.Delay(options.PingFrequency, stoppingToken);
            }
        }
    }
}
