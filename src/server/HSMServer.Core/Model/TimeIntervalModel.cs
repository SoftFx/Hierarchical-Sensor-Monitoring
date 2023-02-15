namespace HSMServer.Core.Model
{
    public enum TimeInterval : byte
    {
        TenMinutes,
        Hour,
        Day,
        Week,
        Month,
        Forever,
        Custom = byte.MaxValue,
    }


    public class TimeIntervalModel
    {
        public TimeInterval TimeInterval { get; init; }

        public long CustomPeriod { get; init; }


        internal bool IsEmpty => TimeInterval == TimeInterval.Custom && CustomPeriod == 0;
    }
}
