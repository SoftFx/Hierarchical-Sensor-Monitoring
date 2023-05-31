using System;
using System.Text.Json.Serialization;

namespace HSMServer.Core.Model
{
    public enum TimeInterval : byte
    {
        TenMinutes,
        Hour,
        Day,
        Week,
        Month,
        OneMinute,
        FiveMinutes,
        FromFolder = byte.MaxValue - 2,
        FromParent = byte.MaxValue - 1,
        Custom = byte.MaxValue,
    }


    public class TimeIntervalModel
    {
        public TimeInterval TimeInterval { get; } = TimeInterval.FromParent;

        public long CustomPeriod { get; }

        [JsonIgnore]
        public bool IsNever => TimeInterval.IsCustom() && CustomPeriod == 0;

        [JsonIgnore]
        public bool IsFromFolder => TimeInterval == TimeInterval.FromFolder;


        [JsonConstructor]
        public TimeIntervalModel(TimeInterval timeInterval, long customPeriod)
        {
            TimeInterval = timeInterval;
            CustomPeriod = customPeriod;
        }

        public TimeIntervalModel(long period)
        {
            CustomPeriod = period;
            TimeInterval = TimeInterval.Custom;
        }


        internal bool TimeIsUp(DateTime time)
        {
            if (TimeInterval.UseCustomPeriod() && CustomPeriod > 0L)
                return (DateTime.UtcNow - time).Ticks > CustomPeriod;

            return DateTime.UtcNow > GetShiftedTime(time);
        }

        public DateTime GetShiftedTime(DateTime time) => TimeInterval switch
        {
            TimeInterval.OneMinute => time.AddMinutes(1),
            TimeInterval.FiveMinutes => time.AddMinutes(5),
            TimeInterval.TenMinutes => time.AddMinutes(10),
            TimeInterval.Hour => time.AddHours(1),
            TimeInterval.Day => time.AddDays(1),
            TimeInterval.Week => time.AddDays(7),
            TimeInterval.Month => time.AddMonths(1),
            TimeInterval.Custom or TimeInterval.FromFolder => DateTime.MaxValue, //for Never 
            _ => throw new NotImplementedException(),
        };


        public override bool Equals(object obj)
        {
            return obj is TimeIntervalModel model && (TimeInterval, CustomPeriod) == (model.TimeInterval, model.CustomPeriod);
        }

        public override int GetHashCode() => (TimeInterval, CustomPeriod).GetHashCode();
    }
}
