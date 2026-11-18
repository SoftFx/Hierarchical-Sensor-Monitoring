using System;
using System.Collections.Concurrent;
using System.Threading;

namespace HSMServer.Model.Agent
{
    /// <summary>
    /// The ONE-TIME download-link store of the agent-installer surface (#1788):
    /// a bearer-authenticated caller issues a short-lived link, and the
    /// redemption endpoint serves the bundle to whoever holds the link — no
    /// Authorization header, no management token on the target machine (ADR
    /// docs/adr/0001-provision-not-push.md: the orchestrator never needs the
    /// raw bearer credential in its shell).
    /// </summary>
    // The link IS the credential for the bytes it carries (the bundle embeds
    // a product access key), so it is single-use and short-lived: redemption
    // removes it atomically (TryRemove), and a timer sweeps the expired
    // leftovers — the RestoreService's session-store pattern. The link holds
    // the BUILT bundle bytes: size/sha256 are computed once, at issue time,
    // and the redemption streams byte-identical content (rebuilding could
    // answer different bytes than the advertised hash — a staged-binary swap
    // between issue and redeem). Singleton: in-memory, cross-request.
    public sealed class AgentInstallerDownloadLinks : IDisposable
    {
        internal static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(10);

        private readonly ConcurrentDictionary<Guid, Link> _links = new();
        private readonly Timer _sweep;

        // Test seam: the clock the expiry decisions read.
        internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

        public AgentInstallerDownloadLinks() =>
            _sweep = new Timer(_ => SweepExpired(), null, LinkLifetime, LinkLifetime);

        public sealed record Link(string FileName, string ContentType, byte[] Bundle)
        {
            public DateTime ExpiresAtUtc { get; init; }
        }


        /// <summary>Issue a link for the given bundle; the token is the URL secret.</summary>
        public (Guid Token, DateTime ExpiresAtUtc) Issue(Link link)
        {
            var token = Guid.NewGuid();
            var expiresAt = UtcNow() + LinkLifetime;

            _links[token] = link with { ExpiresAtUtc = expiresAt };

            return (token, expiresAt);
        }

        /// <summary>
        /// Redeem a link: SINGLE USE — a successful redemption removes it, an
        /// expired one is indistinguishable from an unknown token (the plain
        /// not-found answer; the link must not become an enumeration or
        /// reuse oracle).
        /// </summary>
        public Link TryRedeem(Guid token) =>
            _links.TryRemove(token, out var link) && link.ExpiresAtUtc > UtcNow()
                ? link
                : null;


        private void SweepExpired()
        {
            var cutoff = UtcNow();

            foreach (var (token, link) in _links)
                if (link.ExpiresAtUtc <= cutoff)
                    _links.TryRemove(token, out _);
        }

        public void Dispose() => _sweep.Dispose();
    }
}
