using System;
using System.Collections.Generic;

namespace HSMServer.SensorApi
{
    // Whether a fault of a registration request is worth an Error line of its own (#1749).
    //
    // A fault is answered 500, and a collector sends a package answered 500 again on every
    // cycle, so a fault that repeats (a bug of the server on one sensor, a store that stays
    // down) would write the same line every collect period of every collector it hits, for
    // as long as it lasts. The same fault of the same key is therefore logged once per
    // window and counted in between; the line that follows the window says how many
    // requests went unlogged.
    //
    // "The same fault" is the access key, the type of the first exception of the request
    // and its message. Bounded: at most `capacity` faults are remembered. When a new one
    // does not fit, those whose window has passed are forgotten, and failing that the one
    // logged longest ago; a forgotten fault is simply logged again when it returns.
    //
    // The clock is a count of milliseconds that never goes back (not the wall clock, which
    // an operator can set back and so silence a fault for hours).
    internal sealed class RepeatedFaultLog(TimeSpan window, int capacity, Func<long> elapsedMilliseconds)
    {
        public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(5);
        public const int DefaultCapacity = 256;

        private readonly Dictionary<(Guid Key, Type Type, string Message), Entry> _faults = new(capacity);
        private readonly long _windowMilliseconds = (long)window.TotalMilliseconds;
        private readonly object _lock = new();


        public RepeatedFaultLog() : this(DefaultWindow, DefaultCapacity, () => Environment.TickCount64) { }


        // The part of a log line that says what went unlogged before it; empty when nothing did.
        public static string Unlogged(long count) =>
            count == 0 ? string.Empty : $" {count} more request(s) with the same fault were not logged since the previous line.";


        // How many faults are remembered now.
        public int Count
        {
            get
            {
                lock (_lock)
                    return _faults.Count;
            }
        }


        // True when the fault is to be logged now. `unlogged` is then the number of requests
        // with the same fault that were not logged since the line before.
        public bool IsDue(Guid key, Exception fault, out long unlogged)
        {
            var id = (key, fault.GetType(), fault.Message);
            var now = elapsedMilliseconds();

            lock (_lock)
            {
                if (_faults.TryGetValue(id, out var entry) && now - entry.LoggedAt < _windowMilliseconds)
                {
                    entry.Unlogged++;
                    unlogged = 0;

                    return false;
                }

                unlogged = entry?.Unlogged ?? 0;

                if (entry is null && _faults.Count >= capacity)
                    MakeRoom(now);

                _faults[id] = new Entry { LoggedAt = now };

                return true;
            }
        }

        private void MakeRoom(long now)
        {
            (Guid, Type, string)? oldest = null;
            var oldestAt = long.MaxValue;
            var expired = new List<(Guid, Type, string)>();

            foreach (var (id, entry) in _faults)
            {
                if (now - entry.LoggedAt >= _windowMilliseconds)
                    expired.Add(id);

                if (entry.LoggedAt < oldestAt)
                    (oldest, oldestAt) = (id, entry.LoggedAt);
            }

            foreach (var id in expired)
                _faults.Remove(id);

            if (expired.Count == 0 && oldest is { } victim)
                _faults.Remove(victim);
        }


        private sealed class Entry
        {
            public long LoggedAt;
            public long Unlogged;
        }
    }
}
