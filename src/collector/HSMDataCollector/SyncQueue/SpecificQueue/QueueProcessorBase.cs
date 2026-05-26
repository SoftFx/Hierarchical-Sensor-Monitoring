using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal abstract class QueueProcessorBase<T> : IDisposable
    {
        private Task _task;
        private bool _disposed;

        protected readonly ConcurrentQueue<QueueItem<T>> _queue = new ConcurrentQueue<QueueItem<T>>();
        protected readonly IDataSender _sender;
        protected readonly CollectorOptions _options;
        protected CancellationTokenSource _cancellationTokenSource;
        protected readonly ICollectorLogger _logger;
        protected readonly DataProcessor _queueManager;

        protected int _queueCount;
        private int _stopTimedOut;

        public abstract string QueueName { get; }

        public QueueProcessorBase(CollectorOptions options, DataProcessor queueManager, ICollectorLogger logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _sender  = options.DataSender ?? throw new ArgumentNullException(nameof(options.DataSender));
            _queueManager = queueManager;
            _logger = logger;
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
            _queue.Enqueue(new QueueItem<T>(item));
            Interlocked.Increment(ref _queueCount);

            int result = 0;
            while (Volatile.Read(ref _queueCount) > _options.MaxQueueSize)
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
            foreach(var item in items)
              result += Enqeue(item);

            return result;
        }


        internal DataPackage<T> GetPackage()
        {
            var result = new DataPackage<T>();
            result.Items = Elements().Take(_options.MaxValuesInPackage).Where(Validate).ToList();

            IEnumerable<T> Elements()
            {
                DateTime now = DateTime.UtcNow;

                while (TryDequeue(out QueueItem<T> item))
                {
                    result.AddInfo((now - item.BuildDate).TotalSeconds, 1);

                    yield return item.Value;
                }
            }

            return result;
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


        protected abstract Task ProcessingLoop(CancellationToken token);

        protected int QueueCount => Volatile.Read(ref _queueCount);

        protected bool TryDequeue(out QueueItem<T> item)
        {
            if (!_queue.TryDequeue(out item))
                return false;

            Interlocked.Decrement(ref _queueCount);
            return true;
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

        private void ClearQueue()
        {
            while (TryDequeue(out _)) { }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

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
