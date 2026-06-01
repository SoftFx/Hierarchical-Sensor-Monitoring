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
    internal enum QueueState : byte
    {
        Stopped,
        Running,
        Stopping,
    }


    internal abstract class QueueProcessorBase<T> : IDisposable
    {
        private Task _task;
        private bool _disposed;
        private QueueState _state;
        private readonly Channel<QueueItem<T>> _channel;

        protected ChannelReader<QueueItem<T>> Reader => _channel.Reader;
        protected ChannelWriter<QueueItem<T>> Writer => _channel.Writer;
        protected readonly IDataSender _sender;
        protected readonly CollectorOptions _options;
        protected CancellationTokenSource _cancellationTokenSource;
        protected readonly ICollectorLogger _logger;
        protected readonly DataProcessor _queueManager;

        private readonly object _lifecycleLock = new object();
        protected int _queueCount;
        private int _cleanupContinuationRegistered;

        public abstract string QueueName { get; }

        public QueueProcessorBase(CollectorOptions options, DataProcessor queueManager, ICollectorLogger logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _sender  = options.DataSender ?? throw new ArgumentNullException(nameof(options.DataSender));
            _queueManager = queueManager;
            _logger = logger;

            _channel = Channel.CreateUnbounded<QueueItem<T>>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
        }

        internal bool Start()
        {
            lock (_lifecycleLock)
            {
                if (_disposed)
                {
                    _logger.Error($"{QueueName} queue processor is disposed and cannot be started.");
                    return false;
                }

                if (_state == QueueState.Stopping)
                {
                    _logger.Error($"{QueueName} queue processor is still stopping and cannot be started again yet.");
                    return false;
                }

                if (_state == QueueState.Running)
                {
                    // Defensive: ProcessingLoop should never exit on its own (it loops until cancellation),
                    // but if a subclass override breaks that contract, recover by treating the queue as stopped.
                    // We do NOT call Task.Dispose on a possibly-faulted task — let the GC handle it to avoid
                    // surfacing unobserved exceptions on the finalizer thread.
                    if (_task == null || _task.IsCompleted)
                    {
                        _logger.Error($"{QueueName} queue processor task exited unexpectedly; restarting.");
                        _task = null;
                        _cancellationTokenSource?.Dispose();
                        _cancellationTokenSource = null;
                        _state = QueueState.Stopped;
                        Interlocked.Exchange(ref _cleanupContinuationRegistered, 0);
                    }
                    else
                    {
                        return true;
                    }
                }

                _cancellationTokenSource = new CancellationTokenSource();
                _task = Task.Run(() => ProcessingLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
                _state = QueueState.Running;
                Interlocked.Exchange(ref _cleanupContinuationRegistered, 0);
            }

            return true;
        }

        internal async ValueTask<bool> StopAsync(bool clearQueue = true)
        {
            try
            {
                Task taskToWait;
                CancellationTokenSource tokenSourceToDispose;

                lock (_lifecycleLock)
                {
                    if (_state == QueueState.Stopped)
                    {
                        if (clearQueue)
                            ClearQueue();

                        return true;
                    }

                    if (_state == QueueState.Stopping)
                    {
                        if (clearQueue)
                            ClearQueue();

                        return false;
                    }

                    _state = QueueState.Stopping;
                    taskToWait = _task;
                    tokenSourceToDispose = _cancellationTokenSource;
                }

                try
                {
                    tokenSourceToDispose?.Cancel();

                    if (!taskToWait.IsCompleted)
                    {
                        using (var delayCancellation = new CancellationTokenSource())
                        {
                            var delayTask = Task.Delay(_options.RequestTimeout, delayCancellation.Token);
                            var completedTask = await Task.WhenAny(taskToWait, delayTask).ConfigureAwait(false);

                            if (completedTask != taskToWait)
                            {
                                RegisterCompletionCleanup(taskToWait, tokenSourceToDispose);

                                _logger.Error($"{QueueName} queue processor did not stop within {_options.RequestTimeout}. IDataSender may ignore cancellation.");

                                if (clearQueue)
                                    ClearQueue();

                                return false;
                            }

                            delayCancellation.Cancel();
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
            Interlocked.Increment(ref _queueCount);

            if (!Writer.TryWrite(new QueueItem<T>(item)))
            {
                Interlocked.Decrement(ref _queueCount);
                return 0;
            }

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
            var items = new List<T>(_options.MaxValuesInPackage);
            var maxInspected = _options.MaxValuesInPackage > int.MaxValue / 2
                ? int.MaxValue
                : _options.MaxValuesInPackage * 2;
            var inspected = 0;
            DateTime now = DateTime.UtcNow;

            while (items.Count < _options.MaxValuesInPackage &&
                   inspected < maxInspected &&
                   TryDequeue(out QueueItem<T> item))
            {
                inspected++;
                result.AddInfo((now - item.BuildDate).TotalSeconds, 1);

                if (Validate(item.Value))
                    items.Add(item.Value);
            }

            result.Items = items;
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

        protected bool IsEmpty => Volatile.Read(ref _queueCount) == 0;

        protected ValueTask<bool> WaitToReadAsync(CancellationToken token) => Reader.WaitToReadAsync(token);

        protected Task DelayAfterFailureAsync(CancellationToken token)
        {
            var delay = _options.PackageCollectPeriod > TimeSpan.Zero
                ? _options.PackageCollectPeriod
                : TimeSpan.FromMilliseconds(100);

            return Task.Delay(delay, token);
        }

        protected static void EnsureSendSucceeded(PackageSendingInfo info, CancellationToken token)
        {
            if (info.IsSuccess || string.IsNullOrEmpty(info.Error))
                return;

            if (token.IsCancellationRequested)
                throw new OperationCanceledException(token);

            throw new InvalidOperationException(info.Error);
        }

        protected bool TryDequeue(out QueueItem<T> item)
        {
            if (!Reader.TryRead(out item))
                return false;

            Interlocked.Decrement(ref _queueCount);
            return true;
        }

        private void RegisterCompletionCleanup(Task task, CancellationTokenSource tokenSource)
        {
            if (Interlocked.Exchange(ref _cleanupContinuationRegistered, 1) == 1)
                return;

            task.ContinueWith(_ => CompleteStoppedTask(task, tokenSource, clearQueue: false),
                              CancellationToken.None,
                              TaskContinuationOptions.ExecuteSynchronously,
                              TaskScheduler.Default);
        }

        private void CompleteStoppedTask(Task task, CancellationTokenSource tokenSource, bool clearQueue)
        {
            lock (_lifecycleLock)
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

                _state = QueueState.Stopped;
                Interlocked.Exchange(ref _cleanupContinuationRegistered, 0);
            }
        }

        internal int ClearQueue()
        {
            var count = 0;
            while (TryDequeue(out _))
                count++;

            return count;
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
