using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GLOKON.GuacWS.Server.Guac
{
    internal class GuacDClient : IDuplexPipe, IDisposable
    {
        public const byte DataDelimiter = 0x3b; // Represents ';' in UTF8
        private readonly GuacDOptions options;
        private readonly ILogger<GuacDClient> logger;
        private readonly TcpClient client;
        private readonly Pipe inputPipe;
        private readonly CancellationTokenSource cts;
        private PipeWriter outputWriter;

        private NetworkStream stream;

        public Guid Id { get; }

        public PipeReader Input => inputPipe.Reader;

        public PipeWriter Output => outputWriter;

        public GuacDClient(Guid Id, GuacDOptions options, ILogger<GuacDClient> logger)
        {
            this.Id = Id;
            this.options = options;
            this.logger = logger;

            cts = new CancellationTokenSource();
            client = new TcpClient()
            {
                NoDelay = true,
                SendBufferSize = options.SendBufferSize,
                SendTimeout = options.SendTimeout,
                ReceiveBufferSize = options.ReceiveBufferSize,
                ReceiveTimeout = options.ReceiveTimeout,
            };
            inputPipe = new Pipe(new PipeOptions(useSynchronizationContext: false));
        }

        public void Dispose()
        {
            logger.LogDebug("[{0}] Cleaning up GuacD client", Id);
            cts.Dispose();
            client.Dispose();
        }

        public async Task ConnectAsync()
        {
            logger.LogDebug("[{0}] Connecting GuacD client to {1}:{2}", Id, options.Host, options.Port);
            await client.ConnectAsync(options.Host, options.Port);
            stream = client.GetStream();
            outputWriter = PipeWriter.Create(stream);
        }

        public async Task CloseAsync()
        {
            logger.LogDebug("[{0}] Disconnecting GuacD client", Id);
            cts.Cancel();
            await inputPipe.Writer.CompleteAsync();
            await inputPipe.Reader.CompleteAsync();
            client.Close();
            inputPipe.Reset();
        }

        public async Task RunUntilCloseAsync()
        {
            logger.LogDebug("[{0}] Using pipelines for GuacD", Id);

            try
            {
                while (!cts.IsCancellationRequested)
                {
                    Memory<byte> memory = inputPipe.Writer.GetMemory(options.ReceiveBufferSize);

                    int bytesReceived = await stream.ReadAsync(memory);

                    if (bytesReceived > 0)
                    {
                        inputPipe.Writer.Advance(bytesReceived);

                        FlushResult result = await inputPipe.Writer.FlushAsync(cts.Token);
                        if (result.IsCompleted || result.IsCanceled)
                        {
                            break;
                        }
                    }
                    else
                    {
                        // 0 bytes received, we are at end of stream
                        break;
                    }
                }
            }
            catch (OperationCanceledException) {}
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
            {
                logger.LogError(ex, "[{0}] Error occurred during receiving from GuacD", Id);
            }

            await CloseAsync();
        }
    }
}
