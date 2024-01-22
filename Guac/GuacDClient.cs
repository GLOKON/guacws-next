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
    internal class GuacDClient: IDisposable
    {
        private const byte DataDelimiter = 0x3b; // Represents ';' in UTF8
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
            if (options.UsePipelines)
            {
                logger.LogDebug("[{0}] Using pipelines for GuacD", Id);
                await ReceiveUsingPipelinesAsync();
            }
            else
            {
                logger.LogDebug("[{0}] Using buffers for GuacD", Id);
                await ReceiveUsingBuffersAsync();
            }

            await CloseAsync();
        }

        private async Task ReceiveUsingPipelinesAsync()
        {
            var options = new PipeOptions(useSynchronizationContext: false);
            var pipe = new Pipe(options);
            Task writing = FillPipeAsync(stream, pipe.Writer);
            Task reading = ProcessPipeAsync(pipe.Reader);

            await Task.WhenAll(reading, writing);

            pipe.Reset();
        }

        private async Task ReceiveUsingBuffersAsync()
        {
            var buffer = ArrayPool<byte>.Shared.Rent(options.ReceiveBufferSize * 4);
            int bufferHeadIndex = 0;

            while (!cts.IsCancellationRequested)
            {
                try
                {
                    int bytesReceived = await stream.ReadAsync(buffer, bufferHeadIndex, (buffer.Length - bufferHeadIndex), cts.Token);

                    if (bytesReceived > 0)
                    {
                        int totalReadSize = bytesReceived + bufferHeadIndex;
                        int lastEndingIndex = Array.LastIndexOf(buffer, DataDelimiter, (totalReadSize - 1));
                        int nextDataIndex = 0;

                        if (lastEndingIndex != -1)
                        {
                            nextDataIndex = lastEndingIndex + 1;

                            try
                            {
                                await OnReceiveTextAsync(Encoding.UTF8.GetString(buffer, 0, nextDataIndex));
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "[{0}] There was a problem handling the GuacD message", Id);
                            }
                        }

                        int remainderSize = totalReadSize - nextDataIndex;
                        if (remainderSize > 0)
                        {
                            // Copy remainder to backing buffer
                            Buffer.BlockCopy(buffer, nextDataIndex, buffer, 0, remainderSize);
                            bufferHeadIndex = remainderSize;
                        }
                        else
                        {
                            bufferHeadIndex = 0;
                        }
                    }
                    else
                    {
                        // 0 bytes received, we are at end of stream
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
                {
                    logger.LogError(ex, "[{0}] Error occurred during receiving from GuacD", Id);
                }
            }

            ArrayPool<byte>.Shared.Return(buffer);
        }

        private async Task FillPipeAsync(NetworkStream stream, PipeWriter writer)
        {
            while (!cts.IsCancellationRequested)
            {
                Memory<byte> memory = writer.GetMemory(options.ReceiveBufferSize);

                try
                {
                    int bytesReceived = await stream.ReadAsync(memory);

                    if (bytesReceived > 0)
                    {
                        writer.Advance(bytesReceived);

                        FlushResult result = await writer.FlushAsync();

                        if (result.IsCompleted)
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
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
                {
                    logger.LogError(ex, "[{0}] Error occurred during receiving from GuacD", Id);
                    break;
                }
            }

            await writer.CompleteAsync();
        }

        private async Task ProcessPipeAsync(PipeReader reader)
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    ReadResult result = await reader.ReadAsync();
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    while (TryReadMessage(ref buffer, out string message))
                    {
                        try
                        {
                            await OnReceiveTextAsync(message);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "[{0}] There was a problem handling the GuacD message", Id);
                        }
                    }

                    reader.AdvanceTo(buffer.Start, buffer.End);

                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
                {
                    logger.LogError(ex, "[{0}] Error occurred during processing from GuacD", Id);
                    break;
                }
            }

            await reader.CompleteAsync();
        }

        private bool TryReadMessage(ref ReadOnlySequence<byte> buffer, out string message)
        {
            SequencePosition? position = buffer.PositionOf(DataDelimiter);

            if (position == null)
            {
                message = default;
                return false;
            }

            SequencePosition nextDataStart = buffer.GetPosition(1, position.Value);
            message = Encoding.UTF8.GetString(buffer.Slice(0, nextDataStart));
            buffer = buffer.Slice(nextDataStart);
            return true;
        }

        private async Task OnReceiveTextAsync(string message)
        {
            if (ReceiveTextAsync != null)
            {
                await ReceiveTextAsync(message);
            }
        }
    }
}
