using HSMDatabase.AccessManager.DatabaseEntities;
using System;

namespace HSMServer.Core.Model
{
    public enum OldTimeInterval : byte
    {
        TenMinutes,
        Hour,
        Day,
        Week,
        Month,
        OneMinute,
        FiveMinutes,
        ThreeMonths,
        SixMonths,
        Year,
        FromFolder = byte.MaxValue - 2,
        FromParent = byte.MaxValue - 1,
        Custom = byte.MaxValue,
    }

    public enum TimeInterval : long
    {
        FromFolder = -100,
        FromParent = -10,
        Custom = -1,

        Never = 0,

        OneMinute = 600_000_000,
        FiveMinutes = 3_000_000_000,
        TenMinutes = 6_000_000_000,

        Hour = 36_000_000_000,
        Day = 864_000_000_000,
        Week = 6_048_000_000_000,

        Month = 26_784_000_000_000, // 31 days
        ThreeMonths = 80_352_000_000_000, // 31 * 3
        SixMonths = 160_704_000_000_000, // 31 * 6

        Year = 315_360_000_000_000, //365 days

        Forever = long.MaxValue - 1,
    }


    public class TimeIntervalModel
    {
        public TimeInterval Interval { get; } = TimeInterval.FromParent;

        public long Ticks { get; }


        public bool IsNever => Interval == TimeInterval.Never;

        public bool IsFromFolder => Interval == TimeInterval.FromFolder;

        public bool IsFromParent => Interval == TimeInterval.FromParent;

        public bool UseCustom => Interval is TimeInterval.Custom or TimeInterval.FromFolder;


        public TimeIntervalModel() { }

        public TimeIntervalModel(long ticks)
        {
            Ticks = ticks;
            Interval = TimeInterval.Custom;
        }

        public TimeIntervalModel(TimeIntervalEntity entity) : this((TimeInterval)entity.Interval, entity.Ticks) { }

        public TimeIntervalModel(TimeInterval interval, long ticks)
        {
            Interval = interval;
            Ticks = ticks;
        }


        internal bool TimeIsUp(DateTime time) =>
            UseCustom ? (DateTime.UtcNow - time).Ticks > Ticks
                      : DateTime.UtcNow > GetShiftedTime(time);

        public DateTime GetShiftedTime(DateTime time, int coef = 1) => Interval switch
        {
            TimeInterval.OneMinute or TimeInterval.FiveMinutes or
            TimeInterval.TenMinutes or TimeInterval.Hour or
            TimeInterval.Day or TimeInterval.Week => time.AddTicks((long)Interval * coef),

            TimeInterval.Month => time.AddMonths(coef),
            TimeInterval.ThreeMonths => time.AddMonths(3 * coef),
            TimeInterval.SixMonths => time.AddMonths(6 * coef),

            TimeInterval.Year => time.AddYears(coef),

            TimeInterval.Custom or TimeInterval.FromFolder => time.AddTicks(Ticks * coef),
            TimeInterval.Never or TimeInterval.Forever => DateTime.MaxValue,

            _ => throw new NotImplementedException(),
        };

        internal TimeIntervalEntity ToEntity() => new((long)Interval, Ticks);
    }
}