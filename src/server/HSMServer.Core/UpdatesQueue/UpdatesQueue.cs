using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using HSMCommon.TaskResult;
using NLog;

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

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly Channel<StoreItem> _channel;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _processingTask;

        public event Action<IUpdateRequest> ItemAdded;

        public UpdatesQueue()
        {
            _channel = Channel.CreateBounded<StoreItem>(_channelOptions);
            _processingTask = ProcessQueueAsync(_cts.Token);
        }

        private async Task ProcessQueueAsync(CancellationToken token)
        {
            try
            {
                await foreach (var item in _channel.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        ItemAdded?.Invoke(item.UpdateRequest);
                        if (item.IsAwaitable && !item.TaskCompletionSource.Task.IsCompleted)
                            item.TaskCompletionSource.TrySetResult(TaskResult.Ok);
                    }
                    catch (Exception ex)
                    {
                        if (item.IsAwaitable && !item.TaskCompletionSource.Task.IsCompleted)
                            item.TaskCompletionSource.TrySetResult(TaskResult.FromError(ex.Message));

                        _logger.Error(ex, "Item processing error");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (ChannelClosedException) { }
            catch (Exception ex) 
            {
                _logger.Error(ex, "Queue processing error");
            }
        }

        public async Task<TaskResult> ProcessRequestAsync(IUpdateRequest item, CancellationToken token = default)
        {
            try
            {
                var storeItem = new StoreItem(item, true);
                await _channel.Writer.WriteAsync(storeItem, token).ConfigureAwait(false);

                return await storeItem.TaskCompletionSource.Task.WaitAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return TaskResult.FromError(ex.Message);
            }
        }


        public async Task AddItemAsync(IUpdateRequest item, CancellationToken token = default)
        {
             await _channel.Writer.WriteAsync(new StoreItem(item, false), token).ConfigureAwait(false);
        }

        public async Task AddItemsAsync(IEnumerable<IUpdateRequest> items, CancellationToken token = default)
        {
            foreach (var item in items)
            {
                await AddItemAsync(item, token);
            }
        }

        public void Dispose()
        {
            _channel.Writer.Complete();
            _cts.Cancel();
            _processingTask.Wait(TimeSpan.FromSeconds(1));
            _processingTask.Dispose();
            _cts.Dispose();
            GC.SuppressFinalize(this);
        }

    }

}
