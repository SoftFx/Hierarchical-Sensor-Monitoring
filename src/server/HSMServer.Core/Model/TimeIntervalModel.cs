using Newtonsoft.Json;
using System;

namespace HSMServer.Core.Model
{
    public enum TimeInterval : byte
    {
        TenMinutes,
        Hour,
        Day,
        Week,
        Month,
        Custom = byte.MaxValue,
    }


    public class TimeIntervalModel
    {
        public TimeInterval TimeInterval { get; init; }

        public long CustomPeriod { get; init; }

        internal bool IsEmpty => TimeInterval == TimeInterval.Custom && CustomPeriod == 0; //should be internal or use JsonIgnore


        public TimeIntervalModel() { }

        public TimeIntervalModel(long period)
        {
            CustomPeriod = period;
            TimeInterval = TimeInterval.Custom;
        }


        internal bool TimeIsUp(DateTime time)
        {
            return TimeInterval switch
            {
                TimeInterval.TenMinutes => DateTime.UtcNow > time.AddMinutes(10),
                TimeInterval.Hour => DateTime.UtcNow > time.AddHours(1),
                TimeInterval.Day => DateTime.UtcNow > time.AddDays(1),
                TimeInterval.Week => DateTime.UtcNow > time.AddDays(7),
                TimeInterval.Month => DateTime.UtcNow > time.AddMonths(1),
                TimeInterval.Custom => (DateTime.UtcNow - time).Ticks > CustomPeriod,
                _ => throw new NotImplementedException(),
            };
        }
    }
}
