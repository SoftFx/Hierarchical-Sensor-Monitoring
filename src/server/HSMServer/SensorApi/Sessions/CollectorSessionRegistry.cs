using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.SensorApi.Sessions
{
    // The live collector sessions of this server (#1664): the one place that answers
    // "which collectors are connected", enforces the server-wide cap, and reaches every
    // session at once (the close at shutdown now, pushes later).
    public sealed class CollectorSessionRegistry
    {
        private readonly ConcurrentDictionary<Guid, CollectorSession> _sessions = new();

        // Slots taken, including upgrades that are still in progress. Counted apart from
        // the dictionary so the cap holds while many upgrades race.
        private int _slots;


        // Sessions that completed the upgrade.
        public int Count => _sessions.Count;

        public IReadOnlyList<CollectorSessionInfo> GetSessions() => _sessions.Values.Select(session => session.ToInfo()).ToList();

        // True while the close of some session waits for a frame that is being handled.
        internal bool AnyDeferredClose => _sessions.Values.Any(session => session.HasDeferredClose);


        // Takes a slot before the upgrade is accepted, so a refusal is still an ordinary
        // HTTP status. Every successful call is paired with Release.
        internal bool TryReserve(int maxSessions)
        {
            if (Interlocked.Increment(ref _slots) <= maxSessions)
                return true;

            Interlocked.Decrement(ref _slots);

            return false;
        }

        internal void Register(CollectorSession session) => _sessions[session.Id] = session;

        // `session` is null when the upgrade failed after the slot was taken.
        internal void Release(CollectorSession session)
        {
            if (session is not null)
                _sessions.TryRemove(session.Id, out _);

            Interlocked.Decrement(ref _slots);
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
