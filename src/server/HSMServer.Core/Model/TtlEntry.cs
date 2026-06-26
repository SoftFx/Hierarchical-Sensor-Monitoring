using HSMServer.Core.Model.Policies;

namespace HSMServer.Core.Model
{
    public sealed class TtlEntry
    {
        public TTLPolicy Policy { get; }
        public TimeIntervalModel Interval { get; }

        public TtlEntry(TTLPolicy policy, TimeIntervalModel interval)
        {
            Policy = policy;
            Interval = interval;
        }
    }
}
