using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NLog;
using HSMServer.Core.Model.Requests;

namespace HSMServer.Core.SensorsUpdatesQueue
{
    public sealed class UpdatesQueue : IUpdatesQueue
    {
        private const int MaxQueueSize = 10_000;
        private readonly BoundedChannelOptions _channelOptions = new(MaxQueueSize)
        {
            SingleWriter = false,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        };

        private readonly Channel<BaseRequestModel> _channel;
        private readonly Logger _logger;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _processingTask;

        public event Action<BaseRequestModel> ItemAdded;

        public UpdatesQueue()
        {
            _logger = LogManager.GetCurrentClassLogger();
            _channel = Channel.CreateBounded<BaseRequestModel>(_channelOptions);
            _processingTask = ProcessQueueAsync(_cts.Token);
        }

        private async Task ProcessQueueAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var item = await _channel.Reader.ReadAsync(token).ConfigureAwait(false);
                    ItemAdded?.Invoke(item);
                }
            }
            catch (OperationCanceledException) { }
            catch (ChannelClosedException) { }
            catch (Exception ex)
            {
                _logger.Error(ex, "Update queue failed");
            }

            while (_channel.Reader.TryRead(out var item))
            {
                ItemAdded?.Invoke(item);
            }
        }

        public async Task AddItemAsync(BaseRequestModel item, CancellationToken token = default)
        {
            try
            {
                await _channel.Writer.WriteAsync(item, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.Error(ex, "An error occurred while add item");
            }
        }

        public async Task AddItemsAsync(IEnumerable<BaseRequestModel> items, CancellationToken token = default)
        {
            foreach (var item in items)
            {
                await AddItemAsync(item, token);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _channel.Writer.Complete();

            _processingTask.Wait(TimeSpan.FromSeconds(5));
            _cts.Dispose();
            GC.SuppressFinalize(this);
        }

    }

}
