using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Middleware
{
    // Deprecated-port visibility (#1590): 443 is the primary address and the app listens on a
    // single port, but the legacy 44330/44333 addresses stay published as aliases through the
    // deprecation window. The public port a client used is not the connection port here — a
    // proxy (or a host-port mapping) forwards everything to the one listener — so the request
    // Host header is the only place it survives, and that is what this middleware reads.
    // A request whose Host carries a legacy port logs a rate-limited warning: enough to spot
    // stragglers in the log store, without one line per collector package (they send every
    // few seconds). Port-less https://host traffic logs nothing.
    public sealed class LegacyPortDeprecationMiddleware(RequestDelegate next, ILogger<LegacyPortDeprecationMiddleware> logger)
    {
        // How often the warning repeats while legacy traffic keeps arriving. The pipeline
        // instantiates the middleware once, so the field throttles process-wide.
        internal static readonly TimeSpan ThrottleWindow = TimeSpan.FromMinutes(10);

        private long _lastWarningUtcTicks;

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Host.Port is { } port && IsLegacyPort(port) &&
                ShouldWarn(DateTime.UtcNow.Ticks))
            {
                logger.LogWarning(
                    "Request arrived via the deprecated port {Port} (Host '{Host}'). Ports {LegacySitePort} and {LegacySensorPort} are deprecated aliases of the primary https://<HSM_DOMAIN>/ (443) address and will be removed in a future release; repoint this client.",
                    port, context.Request.Host.Value, KestrelConfig.LegacySitePort, KestrelConfig.LegacySensorPort);
            }

            await next(context);
        }

        internal static bool IsLegacyPort(int port) =>
            port == KestrelConfig.LegacySitePort || port == KestrelConfig.LegacySensorPort;

        // Internal for tests (deterministic clock). First call in a window wins and logs;
        // concurrent calls may also win spuriously (CompareExchange), which only ever
        // produces one extra warning line — harmless for a throttle.
        internal bool ShouldWarn(long nowUtcTicks)
        {
            var last = Volatile.Read(ref _lastWarningUtcTicks);

            if (nowUtcTicks - last < ThrottleWindow.Ticks)
                return false;

            Interlocked.CompareExchange(ref _lastWarningUtcTicks, nowUtcTicks, last);
            return true;
        }
    }
}
