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
        private readonly int _maxMessages;
        private readonly Action<string> _action;

        private readonly ScheduledTask _task;
        private readonly object _lock = new object();

        private readonly List<string> _messagesToDelete = new List<string>();

        public MessageDeduplicator(Action<string> action, TimeSpan window, int maxMessages)
        {
            if (maxMessages <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxMessages), "Max messages must be greater than zero.");

            _action = action;
            _messageCache = new Dictionary<string, (DateTime, int)>();
            _deduplicationWindow = window;
            _maxMessages = maxMessages;

            if (window != TimeSpan.Zero)
            {
                var now = DateTime.UtcNow;

                _task = CollectorScheduler.Schedule(Cleanup, now.Ceil(window) - now, window, ex => _action?.Invoke(ex.ToString()));
            }
        }


        public void AddMessage(string message, TimeSpan? window = null)
        {
            if (_deduplicationWindow == TimeSpan.Zero)
            {
                _action?.Invoke(message);
                return;
            }

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
                    RemoveExpiredMessages(now);
                    EvictOldestMessageIfFull();

                    value = new ValueTuple<DateTime, int> (expiryTime, 0);
                    _messageCache.Add(message, value);
                    _action?.Invoke(message);
                }
            }
        }

        public void Dispose()
        {
            _task?.Dispose();
        }

        private void Cleanup()
        {
            _messagesToDelete.Clear();
            var now = DateTime.UtcNow;
            lock (_lock)
            {
                RemoveExpiredMessages(now);
            }
        }

        private void RemoveExpiredMessages(DateTime now)
        {
            _messagesToDelete.Clear();

            foreach (var item in _messageCache)
            {
                if (item.Value.ExpireTime < now)
                {
                    if (item.Value.Count != 0)
                        _action?.Invoke(BuildMessage(item.Key, item.Value.Count));

                    _messagesToDelete.Add(item.Key);
                }
            }

            foreach (var key in _messagesToDelete)
                _messageCache.Remove(key);
        }

        private void EvictOldestMessageIfFull()
        {
            if (_messageCache.Count < _maxMessages)
                return;

            string oldestKey = null;
            DateTime oldestExpiry = DateTime.MaxValue;

            foreach (var item in _messageCache)
            {
                if (item.Value.ExpireTime < oldestExpiry)
                {
                    oldestKey = item.Key;
                    oldestExpiry = item.Value.ExpireTime;
                }
            }

            if (oldestKey != null)
                _messageCache.Remove(oldestKey);
        }

        private static string BuildMessage(string message, int count)
        {
            return message + (count > 1 ? $" {count} times" : string.Empty);
        }

    }
}
