namespace HSMServer.Core.DataLayer
{
    /// <summary>The result of one background compaction step over the weekly shards (#1801).</summary>
    public enum HistoryShardCompactionOutcome
    {
        /// <summary>No closed shard needs a compaction.</summary>
        NothingDue,

        /// <summary>Another compaction (the manual one) is running; nothing was done.</summary>
        Busy,

        /// <summary>One shard was compacted.</summary>
        Compacted,

        /// <summary>The compaction of one shard failed; it was logged and the shard is retried later.</summary>
        Failed,
    }
}
