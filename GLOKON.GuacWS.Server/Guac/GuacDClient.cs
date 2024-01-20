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
        private byte[] receiveBuffer;

        public Guid Id { get; }

        public event EventHandler<string> ReceiveText;

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
            cts.Dispose();
            client.Dispose();
        }

        public async Task ConnectAsync()
        {
            await client.ConnectAsync(options.Host, options.Port);
            stream = client.GetStream();
        }

        public Task CloseAsync()
        {
            cts.Cancel();
            client.Close();

            return Task.CompletedTask;
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
                var cancellationToken = cts.Token;


                while (!cancellationToken.IsCancellationRequested)
                {
                    int bytesReceived = await stream.ReadAsync(buffer, 0, buffer.Length);

                    // TODO: Read and send events
                }
            }
            catch (OperationCanceledException ocex) { }
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

        private void OnReceiveText(string message)
        {
            ReceiveText?.Invoke(this, message);
        }
    }
}
