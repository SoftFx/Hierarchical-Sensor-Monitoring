namespace HSMDatabase.AccessManager.DatabaseEntities
{
    /// <summary>
    /// A path a sensor was moved away from (#1718, ADR-0018): values and
    /// registrations sent to <see cref="Path"/> under the root product
    /// <see cref="ProductId"/> land in the owning sensor.
    /// </summary>
    public sealed record SensorAliasEntity
    {
        /// <summary>The root product the old path belongs to.</summary>
        public string ProductId { get; init; }

        /// <summary>The old path, relative to the root product (no product name).</summary>
        public string Path { get; init; }

        public long CreationDate { get; init; }

        /// <summary>The last time a value or registration arrived through the alias; persisted at most hourly.</summary>
        public long LastHit { get; init; }
    }
}
