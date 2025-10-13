using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NLog;
using HSMCommon.TaskResult;
using System.Diagnostics;


namespace HSMServer.Core.SensorsUpdatesQueue
{
    public sealed class UpdatesQueue : IUpdatesQueue
    {
        private const int MaxQueueSize = 1_000;
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

        public Stopwatch Stopwatch { get; } = new Stopwatch();

        public int QueueSize => _channel.Reader.Count;

        public string Name { get; }

        private readonly Action<IUpdatesQueue, IUpdateRequest> _action;

        public UpdatesQueue(string name, Action<IUpdatesQueue, IUpdateRequest> action)
        {
            Name = name;
            _action = action;
            _channel = Channel.CreateBounded<StoreItem>(_channelOptions);
            _processingTask = ProcessQueueAsync(_cts.Token);
        }

        private async Task ProcessQueueAsync(CancellationToken token)
        {
            try
            {
                await foreach (var item in _channel.Reader.ReadAllAsync(token))
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        _action?.Invoke(this, item.UpdateRequest);
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
                _logger.Error(ex, "Execution error");
            }
        }

        public async Task<TaskResult> ProcessRequestAsync(IUpdateRequest item, CancellationToken token = default)
        {
            try
            {
                var storeItem = new StoreItem(item, true);
                await _channel.Writer.WriteAsync(storeItem, token);

                return await storeItem.TaskCompletionSource.Task.WaitAsync(token);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Adding error");
                return TaskResult.FromError(ex.Message);
            }
        }


        public async Task AddItemAsync(IUpdateRequest item, CancellationToken token = default)
        {
            try
            {
                await _channel.Writer.WriteAsync(new StoreItem(item, false), token);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Adding error");
            }
        }

        public async Task AddItemsAsync(IEnumerable<IUpdateRequest> items, CancellationToken token = default)
        {
            foreach (var item in items)
            {
                await AddItemAsync(item, token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _channel.Writer.Complete();
            _cts.Cancel();
            await _processingTask?.WaitAsync(TimeSpan.FromSeconds(5));

            _cts.Dispose();
        }

    }

}
