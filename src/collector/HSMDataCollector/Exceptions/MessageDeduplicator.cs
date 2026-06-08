using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using HSMDataCollector.Extensions;
using HSMDataCollector.Threading;


namespace HSMDataCollector.Exceptions
{
    public class MessageDeduplicator : IDisposable
    {
        private readonly ConcurrentDictionary<string, CachedMessage> _messageCache;
        private readonly TimeSpan _deduplicationWindow;
        private readonly int _maxMessages;
        private readonly Action<string> _action;
        private readonly ICollectorScheduler _ownedScheduler;

        private readonly ScheduledTask _task;

        /// <summary>
        /// Compatibility constructor. For non-zero windows this instance creates and owns a
        /// dedicated scheduler worker; zero-window instances do not schedule cleanup work.
        /// Prefer the internal overload when a shared <see cref="ICollectorScheduler"/> is available.
        /// </summary>
        public MessageDeduplicator(Action<string> action, TimeSpan window, int maxMessages)
            : this(action, window, maxMessages, scheduler: null, ownsScheduler: true)
        {
        }

        internal MessageDeduplicator(ICollectorScheduler scheduler, Action<string> action, TimeSpan window, int maxMessages)
            : this(action, window, maxMessages, scheduler ?? throw new ArgumentNullException(nameof(scheduler)), ownsScheduler: false)
        {
        }

        private MessageDeduplicator(Action<string> action, TimeSpan window, int maxMessages, ICollectorScheduler scheduler, bool ownsScheduler)
        {
            if (maxMessages <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxMessages), "Max messages must be greater than zero.");

            _action = action;
            _messageCache = new ConcurrentDictionary<string, CachedMessage>();
            _deduplicationWindow = window;
            _maxMessages = maxMessages;

            // No periodic cleanup runs when window is Zero (AddMessage short-circuits to direct
            // invocation). Avoid spinning up a worker scheduler in that case — it would just idle
            // until Dispose, holding a thread for nothing.
            if (window == TimeSpan.Zero)
                return;

            if (ownsScheduler)
                _ownedScheduler = scheduler ?? new CollectorScheduler();

            var effectiveScheduler = scheduler ?? _ownedScheduler;

            var now = DateTime.UtcNow;
            _task = effectiveScheduler.Schedule(Cleanup, now.Ceil(window) - now, window, ex => _action?.Invoke(ex.ToString()));
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
            var messagesToSend = new List<string>();

            while (true)
            {
                if (_messageCache.TryGetValue(message, out var value))
                {
                    if (value.ExpireTime < now)
                    {
                        if (!TryRemoveExact(message, value))
                            continue;

                        messagesToSend.Add(BuildMessage(message, value.Count + 1));
                        break;
                    }

                    var updatedValue = new CachedMessage(value.ExpireTime, value.Count + 1);
                    if (_messageCache.TryUpdate(message, updatedValue, value))
                        break;
                }
                else
                {
                    RemoveExpiredMessages(now, messagesToSend);
                    EvictOldestMessagesIfFull();

                    if (_messageCache.TryAdd(message, new CachedMessage(expiryTime, 0)))
                    {
                        messagesToSend.Add(message);
                        break;
                    }
                }
            }

            SendMessages(messagesToSend);
        }

        /// <summary>
        /// Releases the cleanup task and, for the compatibility constructor, the owned scheduler.
        /// Like most disposable types, this assumes construction has completed: it must not be called
        /// concurrently with the constructor. (<c>_task</c>/<c>_ownedScheduler</c> are <c>readonly</c>,
        /// so they cannot be marked <c>volatile</c>; the constructor never leaks <c>this</c> to another
        /// thread before they are assigned — the scheduled <see cref="Cleanup"/> callback is delayed and
        /// reads neither field — so no publication barrier is required in practice.)
        /// </summary>
        public void Dispose()
        {
            _task?.Dispose();
            _ownedScheduler?.Dispose();
        }

        private void Cleanup()
        {
            var now = DateTime.UtcNow;
            var messagesToSend = new List<string>();

            RemoveExpiredMessages(now, messagesToSend);

            SendMessages(messagesToSend);
        }

        private void RemoveExpiredMessages(DateTime now, List<string> messagesToSend)
        {
            foreach (var item in _messageCache)
            {
                if (item.Value.ExpireTime < now)
                {
                    if (TryRemoveExact(item.Key, item.Value) && item.Value.Count != 0)
                        messagesToSend.Add(BuildMessage(item.Key, item.Value.Count));
                }
            }
        }

        private void EvictOldestMessagesIfFull()
        {
            if (_messageCache.Count < _maxMessages)
                return;

            string oldestKey = null;
            CachedMessage oldestValue = null;
            DateTime oldestExpiry = DateTime.MaxValue;

            foreach (var item in _messageCache)
            {
                if (item.Value.ExpireTime < oldestExpiry)
                {
                    oldestKey = item.Key;
                    oldestValue = item.Value;
                    oldestExpiry = item.Value.ExpireTime;
                }
            }

            if (oldestKey != null)
                TryRemoveExact(oldestKey, oldestValue);
        }

        private bool TryRemoveExact(string message, CachedMessage value)
        {
            return ((ICollection<KeyValuePair<string, CachedMessage>>)_messageCache)
                .Remove(new KeyValuePair<string, CachedMessage>(message, value));
        }

        private static string BuildMessage(string message, int count)
        {
            return message + (count > 1 ? $" {count} times" : string.Empty);
        }

        private void SendMessages(List<string> messages)
        {
            foreach (var message in messages)
                _action?.Invoke(message);
        }

        private sealed class CachedMessage
        {
            internal CachedMessage(DateTime expireTime, int count)
            {
                ExpireTime = expireTime;
                Count = count;
            }

            internal DateTime ExpireTime { get; }

            internal int Count { get; }
        }

    }
}
