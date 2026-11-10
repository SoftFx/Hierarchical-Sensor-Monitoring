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
    public sealed class CollectorSessionRegistry
    {
        private readonly ConcurrentDictionary<Guid, CollectorSession> _sessions = new();

        // Slots taken, in total and per client address, including upgrades that are still in
        // progress. Counted apart from the dictionary and under one lock, so both limits
        // hold while many upgrades race.
        private readonly object _slotsLock = new();
        private readonly Dictionary<string, AddressSlots> _slotsByAddress = new();
        private int _slots;


        // Sessions that completed the upgrade.
        public int Count => _sessions.Count;

        public IReadOnlyList<CollectorSessionInfo> GetSessions() => _sessions.Values.Select(session => session.ToInfo()).ToList();

        // True while the close of some session waits for a frame that is being handled.
        internal bool AnyDeferredClose => _sessions.Values.Any(session => session.HasDeferredClose);


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
        internal void Release(CollectorSession session, string address)
        {
            var key = AddressKey(address);

            lock (_slotsLock)
            {
                _slots--;

                if (_slotsByAddress.TryGetValue(key, out var slots) && --slots.Held <= 0)
                    _slotsByAddress.Remove(key);
            }

            if (session is not null)
                _sessions.TryRemove(session.Id, out _);
        }

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

        // Starts the close handshake on every session and gives the peers `budget` to
        // answer; whoever has not answered by then is cut off. Used at shutdown, where the
        // web server would otherwise wait for these connections.
        internal async Task CloseAllAsync(WebSocketCloseStatus status, string reason, TimeSpan budget)
        {
            var sessions = _sessions.Values.ToList();

            if (sessions.Count == 0)
                return;

            using var timeout = new CancellationTokenSource(budget);

            await Task.WhenAll(sessions.Select(session => session.SignalCloseAsync(status, reason, timeout.Token)));

            try
            {
                while (!_sessions.IsEmpty)
                    await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
            }
            catch (OperationCanceledException)
            {
                foreach (var session in _sessions.Values)
                    session.Abort(reason);
            }
        }
    }
}
