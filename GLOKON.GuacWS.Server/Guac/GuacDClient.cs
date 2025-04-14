using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace GLOKON.GuacWS.Server.Guac
{
    internal class GuacDClient : IDuplexPipe, IDisposable
    {
        public const byte DataDelimiter = 0x3b; // Represents ';' in UTF8
        private readonly GuacDOptions options;
        private readonly ILogger logger;
        private readonly TcpClient client;
        private readonly Pipe inputPipe;
        private PipeWriter outputWriter;
        private bool isClosed = false;
        private bool isDisposed = false;
        private NetworkStream stream;

        public Guid Id { get; }

        public PipeReader Input => inputPipe.Reader;

        public PipeWriter Output => outputWriter;

        public GuacDClient(Guid id, GuacDOptions options, ILogger logger)
        {
            Id = id;
            this.options = options;
            this.logger = logger;

            client = new TcpClient()
            {
                NoDelay = options.TcpNoDelay,
                SendBufferSize = options.SendBufferSize,
                SendTimeout = options.SendTimeout,
                ReceiveBufferSize = options.ReceiveBufferSize,
                ReceiveTimeout = options.ReceiveTimeout,
            };
            inputPipe = new Pipe(new PipeOptions(useSynchronizationContext: false));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public async Task ConnectAsync()
        {
            if (isDisposed || isClosed)
            {
                logger.LogWarning("[{id}] Attempting to connect to a disposed GuacD client", Id);
                return;
            }

            logger.LogDebug("[{id}] Connecting GuacD client to {host}:{port}", Id, options.Host, options.Port);
            await client.ConnectAsync(options.Host, options.Port);
            stream = client.GetStream();
            outputWriter = PipeWriter.Create(stream);
        }

        public async Task CloseAsync()
        {
            if (isClosed)
            {
                return;
            }

            logger.LogDebug("[{id}] Disconnecting GuacD client", Id);
            await inputPipe.Writer.CompleteAsync();
            await inputPipe.Reader.CompleteAsync();
            client.Close();
            inputPipe.Reset();
            isClosed = true;
        }

        public async Task RunUntilCloseAsync(CancellationToken token)
        {
            logger.LogDebug("[{id}] Using pipelines for GuacD", Id);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    int bytesReceived = await stream.ReadAsync(inputPipe.Writer.GetMemory(options.ReceiveBufferSize), token);

                    if (bytesReceived > 0)
                    {
                        inputPipe.Writer.Advance(bytesReceived);

                        FlushResult result = await inputPipe.Writer.FlushAsync(token).ConfigureAwait(false);
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
            catch (OperationCanceledException)
            {
                // Operation was cancelled, nothing to do
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
            {
                logger.LogError(ex, "[{id}] Error occurred during receiving from GuacD", Id);
            }

            logger.LogDebug("[{id}] Finished running the GuacD client", Id);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (isDisposed)
            {
                return;
            }

            logger.LogDebug("[{id}] Cleaning up GuacD client", Id);
            client.Dispose();
            isDisposed = true;
        }
    }
}
