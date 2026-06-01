using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal abstract class QueueProcessorBase<T> : IDisposable
    {
        private Task _task;
        private bool _disposed;

        private readonly Channel<QueueItem<T>> _channel;

        protected ChannelReader<QueueItem<T>> Reader => _channel.Reader;
        protected ChannelWriter<QueueItem<T>> Writer => _channel.Writer;

        protected readonly IDataSender _sender;
        protected readonly CollectorOptions _options;
        protected CancellationTokenSource _cancellationTokenSource;
        protected readonly ICollectorLogger _logger;
        protected readonly DataProcessor _queueManager;

        private readonly DataPackage<T> _dataPackage;

        private int _stopTimedOut;
        public abstract string QueueName { get; }

        protected abstract Task ProcessingLoop(CancellationToken token);

        public QueueProcessorBase(CollectorOptions options, DataProcessor queueManager, ICollectorLogger logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _sender = options.DataSender ?? throw new ArgumentNullException(nameof(options.DataSender));
            _queueManager = queueManager;
            _logger = logger;

            _dataPackage = new DataPackage<T>(_options.MaxValuesInPackage);

            _channel = Channel.CreateUnbounded<QueueItem<T>>(new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = false,
            });
        }

        internal void Start()
        {
            if (_task != null)
            {
                if (_task.IsCompleted)
                    CompleteStoppedTask(_task, _cancellationTokenSource, clearQueue: false);
                else
                {
                    _logger.Error($"{QueueName} queue processor is still stopping and cannot be started again yet.");
                    return;
                }
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _task = Task.Run(() => ProcessingLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
        }

        internal async ValueTask<bool> StopAsync(bool clearQueue = true)
        {
            try
            {
                if (_task is null)
                {
                    if (clearQueue)
                        ClearQueue();

                    return true;
                }

                var taskToWait = _task;
                var tokenSourceToDispose = _cancellationTokenSource;

                try
                {
                    tokenSourceToDispose?.Cancel();

                    if (!taskToWait.IsCompleted)
                    {
                        var completedTask = await Task.WhenAny(taskToWait, Task.Delay(_options.RequestTimeout)).ConfigureAwait(false);

                        if (completedTask != taskToWait)
                        {
                            if (Interlocked.Exchange(ref _stopTimedOut, 1) == 0)
                                _logger.Error($"{QueueName} queue processor did not stop within {_options.RequestTimeout}. IDataSender may ignore cancellation.");

                            if (clearQueue)
                                ClearQueue();

                            return false;
                        }
                    }

                    await taskToWait.ConfigureAwait(false);
                    return true;
                }
                finally
                {
                    if (taskToWait.IsCompleted)
                        CompleteStoppedTask(taskToWait, tokenSourceToDispose, clearQueue);
                }
            }
            catch (OperationCanceledException)
            {
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
                return true;
            }
        }

        internal virtual int Enqeue(T item)
        {
            return Enqueue(new QueueItem<T>(item));
        }


        internal virtual int Enqueue(QueueItem<T> item)
        {
            return EnqueueCore(item);
        }

        private int EnqueueCore(QueueItem<T> item)
        {
            Writer.TryWrite(item);

            int result = 0;
            while (QueueCount > _options.MaxQueueSize)
            {
                if (!TryDequeue(out _))
                    break;
                result++;
            }

            return result;
        }

        internal virtual int Enqeue(IEnumerable<T> items)
        {
            int result = 0;
            foreach (var item in items)
                result += Enqeue(item);

            return result;
        }


        internal DataPackage<T> GetPackage()
        {
            _dataPackage.Clear();


            while (_dataPackage.Count < _options.MaxValuesInPackage && TryDequeue(out QueueItem<T> item))
            {
                if (Validate(item.Value))
                {
                    _dataPackage.AddValue(item);
                }
            }

            return _dataPackage;
        }

        private bool Validate(T item)
        {
            if (item is BarSensorValueBase bar)
            {
                if (bar.Count <= 0)
                    return false;
            }

            return true;
        }

        protected int QueueCount => Reader.Count;

        protected bool TryDequeue(out QueueItem<T> item)
        {
            if (Reader.TryRead(out item))
                return true;

            return false;
        }

        private void CompleteStoppedTask(Task task, CancellationTokenSource tokenSource, bool clearQueue)
        {
            if (!ReferenceEquals(_task, task))
                return;

            task.Dispose();
            _task = null;

            tokenSource?.Dispose();

            if (ReferenceEquals(_cancellationTokenSource, tokenSource))
                _cancellationTokenSource = null;

            if (clearQueue)
                ClearQueue();

            Interlocked.Exchange(ref _stopTimedOut, 0);
        }

        protected bool IsEmpty => QueueCount == 0;
        private void ClearQueue() { while (TryDequeue(out _)) { } }

        public void Dispose() 
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        { 
            if (_disposed) return;

            if (disposing)
            { 
                try
                {
                    StopAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex) 
                {
                    _logger.Error($"Error during disposal: {ex}");
                }
            }

            _disposed = true;
        }
    }
}