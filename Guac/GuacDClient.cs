using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GLOKON.GuacWS.Server.Guac
{
    internal class GuacDClient: IDisposable
    {
        private readonly GuacDOptions options;
        private readonly ILogger<GuacDClient> logger;
        private readonly TcpClient client;
        private readonly CancellationTokenSource cts;

        private NetworkStream stream;

        public Guid Id { get; }

        public delegate Task ReceiveAsync<T>(T e);
        public ReceiveAsync<string> ReceiveTextAsync;

        public GuacDClient(Guid Id, GuacDOptions options, ILogger<GuacDClient> logger)
        {
            this.Id = Id;
            this.options = options;
            this.logger = logger;
            this.cts = new CancellationTokenSource();
            client = new TcpClient()
            {
                NoDelay = true,
                SendBufferSize = options.SendBufferSize,
                SendTimeout = options.SendTimeout,
                ReceiveBufferSize = options.ReceiveBufferSize,
                ReceiveTimeout = options.ReceiveTimeout,
            };
        }

        public void Dispose()
        {
            logger.LogDebug("[{0}] Cleaning up GuacD client", Id);
            cts.Dispose();
            client.Dispose();
        }

        public Task CloseAsync()
        {
            logger.LogDebug("[{0}] Disconnecting GuacD client", Id);
            cts.Cancel();
            client.Close();

            return Task.CompletedTask;
        }

        public async Task ConnectAsync()
        {
            logger.LogDebug("[{0}] Connecting GuacD client to {1}:{2}", Id, options.Host, options.Port);
            await client.ConnectAsync(options.Host, options.Port);
            stream = client.GetStream();
        }

        public Task SendAsync(string message, CancellationToken cancellationToken)
        {
            return SendAsync(Encoding.UTF8.GetBytes(message), cancellationToken);
        }

        public async Task SendAsync(byte[] message, CancellationToken cancellationToken)
        {
            await stream.WriteAsync(message, 0, message.Length, cancellationToken);
        }

        public async Task ReceiveUntilCloseAsync()
        {
            var buffer = ArrayPool<byte>.Shared.Rent(16384);
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    int bytesReceived = await stream.ReadAsync(buffer, 0, buffer.Length);

                    if (bytesReceived > 0)
                    {
                        await OnReceiveText(Encoding.UTF8.GetString(buffer, 0, bytesReceived));
                    }
                    else
                    {
                        // 0 bytes received, we are at end of stream
                        await CloseAsync();
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
            {
                logger.LogError(ex, "[{0}] Error occurred during receiving from GuacD", Id);
                await CloseAsync();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private async Task OnReceiveText(string message)
        {
            if (ReceiveTextAsync != null)
            {
                await ReceiveTextAsync(message);
            }
        }
    }
}
