using System;
using System.Collections.Concurrent;
using System.Threading;
using HSMDataCollector.Logging;

namespace HSMDataCollector.Client
{
    /// <summary>
    /// The collector's end of the server directive channel (#1198): the directives the server
    /// attaches to its answers (<c>X-Hsm-Directive: name:argument</c>).
    ///
    /// Directives are handled inside the collector; there is no public event (owner decision,
    /// 2026-11-10). The library acts on none of the directives that exist today
    /// (<c>update-available</c>, <c>sensor-disable</c>, <c>sensor-enable</c> are matters of the
    /// agent, which is a native collector host), so each directive is counted, its name is
    /// noted once at debug level, and it is dropped. One the library should act on later is
    /// dispatched from <see cref="Receive"/>, still without a public surface.
    /// </summary>
    internal sealed class ServerDirectives
    {
        internal const string HeaderName = "X-Hsm-Directive";

        // The names come from the network, so what is remembered of them is bounded: this many
        // different names, each cut to this length.
        internal const int MaxNotedNames = 32;
        private const int MaxNameLength = 64;

        private readonly ConcurrentDictionary<string, bool> _notedNames = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        private readonly ICollectorLogger _logger;

        private long _received;

        // The slots of _notedNames that are taken. Kept beside the table because
        // ConcurrentDictionary.Count takes every lock of the table, and once the table is full
        // a name that is not in it would pay that on every answer of the server.
        // INVARIANT: a name is added only by a thread that holds a slot, and a slot is taken
        // only while fewer than MaxNotedNames are taken (compare-and-swap), so the table never
        // holds more than MaxNotedNames names, from however many threads directives arrive.
        private int _notedCount;

        internal ServerDirectives(ICollectorLogger logger)
        {
            _logger = logger;
        }

        /// <summary>How many directives arrived, handled or not.</summary>
        internal long ReceivedCount => Interlocked.Read(ref _received);

        /// <summary>How many different directive names are remembered. For tests.</summary>
        internal int NotedNamesCount => _notedNames.Count;

        private bool TryTakeNoteSlot()
        {
            while (true)
            {
                var taken = Volatile.Read(ref _notedCount);

                if (taken >= MaxNotedNames)
                    return false;

                if (Interlocked.CompareExchange(ref _notedCount, taken + 1, taken) == taken)
                    return true;
            }
        }

        /// <summary>
        /// Takes one directive as the server wrote it: <c>name:argument</c>, or a bare name.
        /// Never throws: a directive must not fail the send whose answer carried it.
        /// </summary>
        internal void Receive(string directive)
        {
            if (string.IsNullOrWhiteSpace(directive))
                return;

            try
            {
                Interlocked.Increment(ref _received);

                // "name:argument", or a bare name. No directive is acted on in this version, so
                // only the name is of interest.
                var colon = directive.IndexOf(':');
                var name = (colon < 0 ? directive : directive.Substring(0, colon)).Trim();

                if (name.Length > MaxNameLength)
                    name = name.Substring(0, MaxNameLength);

                // The same few names arrive with every answer: a lock-free look first.
                if (_notedNames.ContainsKey(name))
                    return;

                if (!TryTakeNoteSlot())
                    return;

                if (_notedNames.TryAdd(name, true))
                    _logger?.Debug($"Server directive '{name}' is not handled by this collector and is ignored.");
                else
                    // Another thread noted the same name in between: the slot is free again.
                    Interlocked.Decrement(ref _notedCount);
            }
            catch (Exception ex)
            {
                _logger?.Debug($"Failed to handle a server directive: {ex.Message}");
            }
        }
    }
}
