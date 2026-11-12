using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Threading;

namespace HSMServer.Core.Model.Sensors
{
    /// <summary>
    /// A path a sensor was moved away from (#1718, ADR-0018). Values and
    /// registrations sent to <see cref="Path"/> under <see cref="ProductId"/>
    /// land in the sensor that owns the alias.
    /// </summary>
    public sealed class SensorAlias
    {
        // Ticks, not DateTime: the last hit is stamped lock-free by the
        // ingestion thread of the alias's product and read by the owning
        // sensor's queue when it persists or sweeps.
        private long _lastHitTicks;
        private long _persistedLastHitTicks;


        /// <summary>The root product the old path belongs to.</summary>
        public Guid ProductId { get; }

        /// <summary>The old path, relative to the root product.</summary>
        public string Path { get; }

        public DateTime CreationDate { get; }

        public DateTime LastHit => new(Interlocked.Read(ref _lastHitTicks), DateTimeKind.Utc);

        /// <summary>The newest of creation and the last hit: what expiry is measured from.</summary>
        public DateTime LastActivity => LastHit > CreationDate ? LastHit : CreationDate;

        internal bool HasUnpersistedHit => Interlocked.Read(ref _lastHitTicks) - Interlocked.Read(ref _persistedLastHitTicks) >= PersistHitAfter.Ticks;

        // A hit is persisted at most this often: an alias fed every second must
        // not turn into one entity write per value.
        internal static TimeSpan PersistHitAfter { get; } = TimeSpan.FromHours(1);


        internal SensorAlias(Guid productId, string path, DateTime creationDate)
        {
            ProductId = productId;
            Path = path;
            CreationDate = creationDate;
        }

        internal SensorAlias(SensorAliasEntity entity)
        {
            ProductId = Guid.TryParse(entity.ProductId, out var productId) ? productId : Guid.Empty;
            Path = entity.Path;
            CreationDate = new DateTime(entity.CreationDate, DateTimeKind.Utc);
            _lastHitTicks = entity.LastHit;
            _persistedLastHitTicks = entity.LastHit;
        }


        internal bool Matches(Guid productId, string path) =>
            ProductId == productId && string.Equals(Path, path, StringComparison.Ordinal);

        internal void Hit(DateTime utcNow)
        {
            var ticks = utcNow.Ticks;
            var current = Interlocked.Read(ref _lastHitTicks);

            // Newest wins: two ingestion threads may race on one alias.
            while (ticks > current)
            {
                var observed = Interlocked.CompareExchange(ref _lastHitTicks, ticks, current);

                if (observed == current)
                    break;

                current = observed;
            }
        }

        // Pure: the hit counts as persisted only once the entity write has
        // returned (MarkPersisted), so a failed write is retried by the next
        // alias pass instead of waiting out PersistHitAfter.
        internal SensorAliasEntity ToEntity() => new()
        {
            ProductId = ProductId.ToString(),
            Path = Path,
            CreationDate = CreationDate.Ticks,
            LastHit = Interlocked.Read(ref _lastHitTicks),
        };

        internal void MarkPersisted(long lastHitTicks)
        {
            var current = Interlocked.Read(ref _persistedLastHitTicks);

            while (lastHitTicks > current)
            {
                var observed = Interlocked.CompareExchange(ref _persistedLastHitTicks, lastHitTicks, current);

                if (observed == current)
                    break;

                current = observed;
            }
        }
    }
}
