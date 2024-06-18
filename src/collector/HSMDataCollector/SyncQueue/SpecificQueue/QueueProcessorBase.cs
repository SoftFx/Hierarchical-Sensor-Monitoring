using HSMDataCollector.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    abstract class QueueProcessorBase<T> : IDisposable
    {
        private readonly TimeSpan _disposingTimeout = TimeSpan.FromSeconds(5);
        private Task _task;
        private bool _disposed;

        protected readonly ConcurrentQueue<T> _queue;
        protected readonly IDataSender _sender;
        protected readonly CollectorOptions _options;
        protected CancellationTokenSource _cancellationTokenSource;

        public QueueProcessorBase(CollectorOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _sender  = options.DataSender ?? throw new ArgumentNullException(nameof(options.DataSender));
        }

        public void Init()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _task = Task.Run(() => ProcessingLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
        }

        public void Stop()
        {
            _cancellationTokenSource?.Cancel();
            _task?.Wait(_disposingTimeout);
            _task?.Dispose();
            _cancellationTokenSource?.Dispose();
        }

        public virtual int Enqeue(T item)
        {
            _queue.Enqueue(item);

            int result = 0;
            while(_queue.Count > _options.MaxQueueSize)
            {
                _queue.TryDequeue(out _);
                result++;
            }

            return result;
        }

        public virtual int Enqeue(IEnumerable<T> items)
        {
            int result = 0;
            foreach(var item in items)
              result += Enqeue(item);

            return result;
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
