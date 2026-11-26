using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.SensorApi.Sessions
{
    // Why an upgrade did not get a slot.
    internal enum SlotRefusal
    {
        None,
        ServerFull,
        AddressFull,
    }


    // The live collector sessions of this server (#1664): the one place that answers
    // "which collectors are connected", enforces the server-wide cap, and reaches every
    // session at once (the close at shutdown now, pushes later).
    //
    // Table connections (#1845) and file connections (#1884) are kept apart: one of each at most
    // per ordinary session, tied to it. They take no slot of their own, so the cap and the limit
    // per address count collectors, and a server holds at most three times MaxSessions sockets.
    public sealed class CollectorSessionRegistry
    {
        private readonly ConcurrentDictionary<Guid, CollectorSession> _sessions = new();

        // Slots taken, in total and per client address, including upgrades that are still in
        // progress. Counted apart from the dictionary and under one lock, so both limits
        // hold while many upgrades race.
        private readonly object _slotsLock = new();
        private readonly Dictionary<string, AddressSlots> _slotsByAddress = new();
        private int _slots;

        // Table and file connections: by their own id, and the one of each kind of each ordinary
        // session. The pair maps are changed under their lock only; a session leaves _sessions
        // before its pairs are looked at, so a paired connection is never attached to a session
        // that has ended.
        private readonly ConcurrentDictionary<Guid, CollectorSession> _tables = new();
        private readonly ConcurrentDictionary<Guid, CollectorSession> _files = new();
        private readonly object _pairLock = new();
        private readonly Dictionary<Guid, CollectorSession> _tableBySession = new();
        private readonly Dictionary<Guid, CollectorSession> _fileBySession = new();

        // The table connection that last stored a block of each (product, producer id): at most one
        // entry per live table connection (a connection's entry goes when it notes another producer
        // or ends), so the map is bounded by the connections. Conflicts between two live connections
        // are reported for the whole server at most once a minute, with the count since the last line.
        private readonly object _producerLock = new();
        private readonly Dictionary<(Guid Product, Guid Producer), CollectorSession> _producers = new();
        private long _conflictWarnedAtTicks;
        private long _conflictsSinceWarn;


        // Sessions that completed the upgrade.
        public int Count => _sessions.Count;

        // Table connections that completed the upgrade.
        public int TableConnectionCount => _tables.Count;

        // File connections that completed the upgrade.
        public int FileConnectionCount => _files.Count;

        public IReadOnlyList<CollectorSessionInfo> GetSessions() => _sessions.Values.Select(session => session.ToInfo()).ToList();

        // True while the close of some session or table connection waits for a frame that is being handled.
        internal bool AnyDeferredClose => _sessions.Values.Concat(_tables.Values).Concat(_files.Values).Any(session => session.HasDeferredClose);


        // Takes a slot before the upgrade is accepted, so a refusal is still an ordinary
        // HTTP status. Every reservation that succeeded is paired with Release for the same
        // address. `maxPerAddress` of zero means no limit per address.
        //
        // The limit per address is looked at first: an address that is over its own limit is
        // told so, also when the server happens to be full as well. `worthLogging` is true at
        // most once a minute per address for that refusal, so one noisy address cannot keep
        // another one out of the log.
        internal SlotRefusal TryReserve(string address, int maxSessions, int maxPerAddress, long utcNowTicks, out bool worthLogging)
        {
            var key = AddressKey(address);

            worthLogging = false;

            lock (_slotsLock)
            {
                _slotsByAddress.TryGetValue(key, out var slots);

                if (maxPerAddress > 0 && slots is not null && slots.Held >= maxPerAddress)
                {
                    if (utcNowTicks - slots.WarnedAtTicks >= TimeSpan.TicksPerMinute)
                    {
                        slots.WarnedAtTicks = utcNowTicks;
                        worthLogging = true;
                    }

                    return SlotRefusal.AddressFull;
                }

                if (_slots >= maxSessions)
                    return SlotRefusal.ServerFull;

                if (slots is null)
                    _slotsByAddress[key] = slots = new AddressSlots();

                slots.Held++;
                _slots++;

                return SlotRefusal.None;
            }
        }

        internal void Register(CollectorSession session) => _sessions[session.Id] = session;

        // `session` is null when the upgrade failed after the slot was taken. The slot is
        // freed before the session leaves the dictionary: whoever sees the session gone can
        // rely on its slot being free.
        //
        // Returns the session's table and file connections, which the caller closes: a paired
        // connection lives only as long as its session.
        internal (CollectorSession Table, CollectorSession File) Release(CollectorSession session, string address)
        {
            var key = AddressKey(address);

            lock (_slotsLock)
            {
                _slots--;

                if (_slotsByAddress.TryGetValue(key, out var slots) && --slots.Held <= 0)
                    _slotsByAddress.Remove(key);
            }

            if (session is null)
                return (null, null);

            _sessions.TryRemove(session.Id, out _);

            lock (_pairLock)
            {
                _tableBySession.Remove(session.Id, out var table);
                _fileBySession.Remove(session.Id, out var file);

                return (table, file);
            }
        }

        // The live ordinary session with this id; null when there is none.
        internal CollectorSession FindSession(Guid id) => _sessions.TryGetValue(id, out var session) && session.Kind == SessionKind.Session ? session : null;

        // Makes `paired` (a table or a file connection) the connection of its kind of its session.
        // False when the session has ended meanwhile. `replaced` is the connection it takes the
        // place of, which the caller closes.
        internal bool TryAttachPaired(CollectorSession paired, out CollectorSession replaced)
        {
            replaced = null;

            var (bySession, all) = PairMaps(paired);

            lock (_pairLock)
            {
                if (!_sessions.ContainsKey(paired.PairedSessionId))
                    return false;

                bySession.TryGetValue(paired.PairedSessionId, out replaced);
                bySession[paired.PairedSessionId] = paired;
                all[paired.Id] = paired;

                return true;
            }
        }

        // The paired connection has ended. Its pair entry goes only if it still points at it (a
        // replaced connection ends after its successor took its place).
        internal void DetachPaired(CollectorSession paired)
        {
            var (bySession, all) = PairMaps(paired);

            lock (_pairLock)
            {
                if (bySession.TryGetValue(paired.PairedSessionId, out var current) && ReferenceEquals(current, paired))
                    bySession.Remove(paired.PairedSessionId);

                all.TryRemove(paired.Id, out _);
            }

            if (!paired.IsTable)
                return;

            var table = paired;

            lock (_producerLock)
            {
                if (table.LastProducer is { } producer && _producers.TryGetValue(producer, out var owner) && ReferenceEquals(owner, table))
                    _producers.Remove(producer);
            }
        }

        // A table connection stored (or was answered a duplicate for) a block of `producer` in
        // `product`. Returns another live table connection that did the same for that producer, if
        // there is one: two collectors with one producer id make each other's blocks duplicates, and
        // the second one's rows are lost. A connection of the same session (the one a reconnect
        // replaces) and one that is closing do not count. `worthLogging` is true at most once a
        // minute for the whole server; `conflicts` is then the number of conflicts since the last
        // line, this one included.
        internal CollectorSession NoteProducer(CollectorSession table, Guid product, Guid producer, long utcNowTicks, out bool worthLogging, out long conflicts)
        {
            var key = (product, producer);

            worthLogging = false;
            conflicts = 0;

            lock (_producerLock)
            {
                if (table.LastProducer is { } previous && previous != key &&
                    _producers.TryGetValue(previous, out var previousOwner) && ReferenceEquals(previousOwner, table))
                    _producers.Remove(previous);

                table.LastProducer = key;

                if (_producers.TryGetValue(key, out var owner) && !ReferenceEquals(owner, table) && _tables.ContainsKey(owner.Id) &&
                    owner.PairedSessionId != table.PairedSessionId && !owner.Ending.IsCancellationRequested)
                {
                    _conflictsSinceWarn++;

                    if (utcNowTicks - _conflictWarnedAtTicks >= TimeSpan.TicksPerMinute)
                    {
                        _conflictWarnedAtTicks = utcNowTicks;
                        worthLogging = true;
                        conflicts = _conflictsSinceWarn;
                        _conflictsSinceWarn = 0;
                    }

                    return owner;
                }

                _producers[key] = table;

                return null;
            }
        }

        private (Dictionary<Guid, CollectorSession> BySession, ConcurrentDictionary<Guid, CollectorSession> All) PairMaps(CollectorSession paired) =>
            paired.IsFile ? (_fileBySession, _files) : (_tableBySession, _tables);

        // What "one address" means for the limit. An IPv4 address is itself. An IPv6 host
        // usually owns a whole /64 and can pick any source address in it, so the limit is
        // kept per /64: otherwise one host could hold one session per address.
        internal static string AddressKey(string address)
        {
            if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetworkV6 || ip.IsIPv4MappedToIPv6)
                return address ?? string.Empty;

            var bytes = ip.GetAddressBytes();

            Array.Clear(bytes, 8, 8);

            return $"{new IPAddress(bytes)}/64";
        }


        private sealed class AddressSlots
        {
            public int Held;

            public long WarnedAtTicks;
        }

        // Starts the close handshake on every session, table and file connection and gives the peers
        // `budget` to answer; whoever has not answered by then is cut off. Used at shutdown,
        // where the web server would otherwise wait for these connections.
        internal async Task CloseAllAsync(WebSocketCloseStatus status, string reason, TimeSpan budget)
        {
            var sessions = _sessions.Values.Concat(_tables.Values).Concat(_files.Values).ToList();

            if (sessions.Count == 0)
                return;

            using var timeout = new CancellationTokenSource(budget);

            await Task.WhenAll(sessions.Select(session => session.SignalCloseAsync(status, reason, timeout.Token)));

            try
            {
                while (!_sessions.IsEmpty || !_tables.IsEmpty || !_files.IsEmpty)
                    await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
            }
            catch (OperationCanceledException)
            {
                foreach (var session in _sessions.Values.Concat(_tables.Values).Concat(_files.Values))
                    session.Abort(reason);
            }
        }
    }
}
