using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    /// agent, which is a native collector host), so each unknown directive is noted once at
    /// debug level and dropped. One the library should act on later gets a row in
    /// <see cref="Handlers"/>, still without a public surface.
    /// </summary>
    internal sealed class ServerDirectives
    {
        internal const string HeaderName = "X-Hsm-Directive";

        // How many different unknown directive names are noted before the notes stop. The
        // names come from the network, so the memory they may take is bounded.
        private const int MaxNotedNames = 32;

        // directive name -> what the collector does with its argument. Empty in this version.
        private static readonly Dictionary<string, Action<string>> Handlers =
            new Dictionary<string, Action<string>>(StringComparer.Ordinal);

        private readonly ConcurrentDictionary<string, bool> _notedNames = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        private readonly ICollectorLogger _logger;

        private long _received;

        internal ServerDirectives(ICollectorLogger logger)
        {
            _logger = logger;
        }

        /// <summary>How many directives arrived, handled or not.</summary>
        internal long ReceivedCount => Interlocked.Read(ref _received);

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

                var colon = directive.IndexOf(':');
                var name = (colon < 0 ? directive : directive.Substring(0, colon)).Trim();
                var argument = colon < 0 ? string.Empty : directive.Substring(colon + 1).Trim();

                if (Handlers.TryGetValue(name, out var handler))
                {
                    handler(argument);
                    return;
                }

                if (_notedNames.Count < MaxNotedNames && _notedNames.TryAdd(name, true))
                    _logger?.Debug($"Server directive '{name}' is not handled by this collector and is ignored.");
            }
            catch (Exception ex)
            {
                _logger?.Debug($"Failed to handle a server directive: {ex.Message}");
            }
        }
    }
}
