using System;

namespace HSMDatabase.LevelDB.Sealed
{
    /// <summary>
    /// The sealing job stopped on a week that an operator has to look at (#1804, review S1/T1/T3): a chunk directory
    /// without <c>hsm-sealed</c> that may hold the only copy of the week, a keep time that cannot be read, or a chunk that
    /// cannot be read while the job needs it. Nothing irreversible was done; the reason says what to do.
    /// </summary>
    public sealed class WeekNeedsAttentionException(string week, string reason, Exception inner = null) : InvalidOperationException(reason, inner)
    {
        public string Week { get; } = week;
    }
}
