using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
        private readonly Dictionary<string, int> _slotsByAddress = new();
        private int _slots;


        // Sessions that completed the upgrade.
        public int Count => _sessions.Count;

        public IReadOnlyList<CollectorSessionInfo> GetSessions() => _sessions.Values.Select(session => session.ToInfo()).ToList();

        // True while the close of some session waits for a frame that is being handled.
        internal bool AnyDeferredClose => _sessions.Values.Any(session => session.HasDeferredClose);


        // Takes a slot before the upgrade is accepted, so a refusal is still an ordinary
        // HTTP status. Every reservation that succeeded is paired with Release.
        // `maxPerAddress` of zero means no limit per address.
        internal SlotRefusal TryReserve(string address, int maxSessions, int maxPerAddress)
        {
            lock (_slotsLock)
            {
                if (_slots >= maxSessions)
                    return SlotRefusal.ServerFull;

                _slotsByAddress.TryGetValue(address, out var held);

                if (maxPerAddress > 0 && held >= maxPerAddress)
                    return SlotRefusal.AddressFull;

                _slots++;
                _slotsByAddress[address] = held + 1;

                return SlotRefusal.None;
            }
        }

        internal void Register(CollectorSession session) => _sessions[session.Id] = session;

        // `session` is null when the upgrade failed after the slot was taken.
        internal void Release(CollectorSession session, string address)
        {
            if (session is not null)
                _sessions.TryRemove(session.Id, out _);

            lock (_slotsLock)
            {
                _slots--;

                if (_slotsByAddress.TryGetValue(address, out var held))
                {
                    if (held > 1)
                        _slotsByAddress[address] = held - 1;
                    else
                        _slotsByAddress.Remove(address);
                }
            }
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
