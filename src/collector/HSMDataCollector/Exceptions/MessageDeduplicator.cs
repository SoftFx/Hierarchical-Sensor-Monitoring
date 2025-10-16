using System;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using HSMDataCollector.Extensions;
using HSMDataCollector.Threading;


namespace HSMDataCollector.Exceptions
{
    public class MessageDeduplicator : IDisposable
    {
        private readonly Dictionary<string, (DateTime ExpireTime, int Count)> _messageCache;
        private readonly TimeSpan _deduplicationWindow;
        private readonly Action<string> _action;

        private readonly Task _task;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly object _lock = new object();

        private readonly List<string> _messagesToDelete = new List<string>();

        public MessageDeduplicator(Action<string> action, TimeSpan window)
        {
            _action = action;
            _messageCache = new Dictionary<string, (DateTime, int)>();
            _deduplicationWindow = window;

            if (window != TimeSpan.Zero)
            {
                _cancellationTokenSource = new CancellationTokenSource();
                var now = DateTime.UtcNow;

                _task = PeriodicTask.Run(Cleanup, now.Ceil(window) - now, window, _cancellationTokenSource.Token);
            }
        }


        public void AddMessage(string message, TimeSpan? window = null)
        {
            if (_deduplicationWindow == TimeSpan.Zero)
                _action?.Invoke(message);

            var now = DateTime.UtcNow;
            var expiryTime = now + (window ?? _deduplicationWindow);
            lock (_lock)
            {
                if (_messageCache.TryGetValue(message, out var value))
                {
                    if (value.ExpireTime < now)
                    {
                        _messageCache.Remove(message);
                        _action?.Invoke(BuildMessage(message, value.Count + 1));
                    }
                    else
                    {
                        _messageCache[message] = new ValueTuple<DateTime, int>(value.ExpireTime, value.Count + 1);
                    }
                }
                else
                {
                    value = new ValueTuple<DateTime, int> (expiryTime, 0);
                    _messageCache.Add(message, value);
                    _action?.Invoke(message);
                }
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _task?.Wait();
            _task?.Dispose();
            _cancellationTokenSource?.Dispose();
        }

        private void Cleanup()
        {
            _messagesToDelete.Clear();
            var now = DateTime.UtcNow;
            lock (_lock)
            {
                foreach (var item in _messageCache)
                {
                    if (item.Value.ExpireTime < now)
                    {
                        if (item.Value.Count != 0)
                        {
                            _action?.Invoke(BuildMessage(item.Key, item.Value.Count));
                        }

                        _messagesToDelete.Add(item.Key);
                    }
                }

                foreach (var key in _messagesToDelete)
                    _messageCache.Remove(key);
            }
        }

        private static string BuildMessage(string message, int count)
        {
            return message + (count > 1 ? $" {count} times" : string.Empty);
        }

    }
}
