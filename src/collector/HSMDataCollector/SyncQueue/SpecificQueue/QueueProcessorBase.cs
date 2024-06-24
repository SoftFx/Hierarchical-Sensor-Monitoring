using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal abstract class QueueProcessorBase<T> : IDisposable
    {
        private readonly TimeSpan _disposingTimeout = TimeSpan.FromSeconds(5);
        private Task _task;
        private bool _disposed;

        protected abstract string QueueName { get; }

        protected readonly ConcurrentQueue<QueueItem<T>> _queue = new ConcurrentQueue<QueueItem<T>>();
        protected readonly IDataSender _sender;
        protected readonly CollectorOptions _options;
        protected CancellationTokenSource _cancellationTokenSource;
        protected readonly ICollectorLogger _logger;
        protected readonly DataProcessor _queueManager;


        public QueueProcessorBase(CollectorOptions options, DataProcessor queueManager, ICollectorLogger logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _sender  = options.DataSender ?? throw new ArgumentNullException(nameof(options.DataSender));
            _queueManager = queueManager;
            _logger = logger;
        }

        internal void Init()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _task = Task.Run(() => ProcessingLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
        }

        internal void Stop()
        {
            _cancellationTokenSource?.Cancel();
            _task?.Wait(_disposingTimeout);
            _task?.Dispose();
            _cancellationTokenSource?.Dispose();
        }

        internal virtual int Enqeue(T item)
        {
            _queue.Enqueue(new QueueItem<T>(item));

            int result = 0;
            while(_queue.Count >= _options.MaxQueueSize)
            {
                _queue.TryDequeue(out _);
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
            result.Items = Elements().Take(_options.MaxValuesInPackage).Where(s => Validate(s));

            IEnumerable<T> Elements()
            {
                DateTime now = DateTime.UtcNow;

                while (_queue.TryDequeue(out QueueItem<T> item))
                {
                    result.AddInfo((now - item.BuildDate).TotalSeconds, 1);

                    yield return item.Value;
                }
            }

            return result;
        }

        private static bool Validate(T item)
        {
            if (item is BarSensorValueBase bar)
            {
                if (bar.Count <= 0)
                    return false;
            }

            return true;
        }

        internal T GetItem(out PackageInfo info)
        {
            if (_queue.TryDequeue(out QueueItem<T> item))
            {
                info = new PackageInfo((DateTime.Now - item.BuildDate).Milliseconds, 1);

                return item.Value;
            }
            else
            {
                info = default;
                return default;
            }
        }

        protected abstract Task ProcessingLoop(CancellationToken token);

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
                _cancellationTokenSource?.Cancel();
                _task?.Wait(_disposingTimeout);
                _task?.Dispose();
                _cancellationTokenSource?.Dispose();
            }

            _disposed = true;
        }
    }
}
