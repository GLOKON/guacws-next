using GLOKON.GuacWS.Server.Infrastructure;
using GLOKON.GuacWS.Server.Middlewares;
using Microsoft.AspNetCore.Http;
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
        private readonly TcpClient client;
        private readonly CancellationTokenSource cts;

        private Task messageHandler;

        private NetworkStream stream;
        private byte[] receiveBuffer;

        public event EventHandler<string> ReceiveText;

        public GuacDClient(GuacDOptions options)
        {
            this.options = options;
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
            messageHandler = Task.Run(ReceiveUntilCloseAsync);
        }

        public Task CloseAsync()
        {
            cts.Cancel();
            client.Close();

            return Task.CompletedTask;
        }

        public Task SendTextAsync(string message, CancellationToken cancellationToken)
        {
            return SendAsync(Encoding.UTF8.GetBytes(message), cancellationToken);
        }

        public async Task SendAsync(byte[] message, CancellationToken cancellationToken)
        {
            await stream.WriteAsync(message, 0, message.Length, cancellationToken);
        }

        private void OnReceiveText(string message)
        {
            ReceiveText?.Invoke(this, message);
        }

        private async Task ReceiveUntilCloseAsync()
        {
            var buffer = ArrayPool<byte>.Shared.Rent(16384);
            try
            {
                var cancellationToken = cts.Token;


                while (!cancellationToken.IsCancellationRequested)
                {
                    int bytesReceived = await stream.ReadAsync(buffer, 0, buffer.Length);
                    OnReceiveText()
                    // TODO: Read and send events
                }
            }
            catch (OperationCanceledException ocex) { }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
            {
                await CloseAsync();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
