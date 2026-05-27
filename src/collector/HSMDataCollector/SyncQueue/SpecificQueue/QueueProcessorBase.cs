using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;


namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal abstract class QueueProcessorBase<T> : IDisposable
    {
        private Task _task;
        private bool _disposed;
        private int _itemCount;

        private readonly Channel<QueueItem<T>> _channel;
        protected ChannelReader<QueueItem<T>> Reader => _channel.Reader;
        protected ChannelWriter<QueueItem<T>> Writer => _channel.Writer;

        protected readonly IDataSender _sender;
        protected readonly CollectorOptions _options;
        protected CancellationTokenSource _cancellationTokenSource;
        protected readonly ICollectorLogger _logger;
        protected readonly DataProcessor _queueManager;

        public abstract string QueueName { get; }

        public QueueProcessorBase(CollectorOptions options, DataProcessor queueManager, ICollectorLogger logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _sender  = options.DataSender ?? throw new ArgumentNullException(nameof(options.DataSender));
            _queueManager = queueManager;
            _logger = logger;

            _channel = Channel.CreateUnbounded<QueueItem<T>>(new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = true,
            });
        }

        internal void Start()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _task = Task.Run(() => ProcessingLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
        }

        internal async ValueTask StopAsync(bool clearQueue = true)
        {
            try
            {
                if (_task is null)
                {
                    if (clearQueue)
                        ClearQueue();

                    return;
                }

                _cancellationTokenSource?.Cancel();

                try
                {
                    await _task.ConfigureAwait(false);
                }
                finally
                {
                    _task.Dispose();
                    _task = null;

                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;

                    if (clearQueue)
                        ClearQueue();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }
        }

        internal virtual int Enqeue(T item)
        {
            Writer.TryWrite(new QueueItem<T>(item));
            Interlocked.Increment(ref _itemCount);

            int result = 0;
            while (Volatile.Read(ref _itemCount) > _options.MaxQueueSize)
            {
                if (!TryDequeueCore(out _))
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

        protected int QueueCount => Volatile.Read(ref _itemCount);

        protected bool TryDequeue(out QueueItem<T> item)
        {
            if (!TryDequeueCore(out item))
                return false;

            Interlocked.Decrement(ref _itemCount);
            return true;
        }

        private bool TryDequeueCore(out QueueItem<T> item) => Reader.TryRead(out item);

        protected bool IsEmpty => Volatile.Read(ref _itemCount) == 0;

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
