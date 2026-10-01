using HSMCommon.Model;

namespace HSMServer.Core.Model
{
    // What TryAddValue did with a value (#1441). The bounded in-memory cache
    // holds only the ordered suffix of a sensor's values, so an accepted value
    // that is OLDER than the cached newest one cannot enter it — but it is
    // accepted data and must still reach the database; the caller
    // (TreeValuesCache) uses the result to decide which value to persist.
    internal enum AddValueKind
    {
        // The value entered the cache as the new newest (or is a timeout
        // marker / the very first value). Persist the cache's newest value.
        Cached,

        // The value folded into the cached newest one (aggregate-values mode,
        // equal content): nothing new to persist separately.
        Aggregated,

        // The value is older than the cached newest value. It was accepted
        // but cannot live in the cache; persist OutOfOrderValue directly.
        OutOfOrder,
    }

    internal readonly record struct AddValueResult(AddValueKind Kind, BaseValue OutOfOrderValue)
    {
        public static AddValueResult Cached { get; } = new(AddValueKind.Cached, null);

        public static AddValueResult Aggregated { get; } = new(AddValueKind.Aggregated, null);

        // The value to persist directly: the VALIDATED, statistics-applied
        // instance from inside TryAddValue — not the caller's raw reference,
        // which never went through CalculateStatistics/EMA tinting.
        public static AddValueResult OutOfOrder(BaseValue value) => new(AddValueKind.OutOfOrder, value);
    }
}
