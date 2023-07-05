using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using HSMServer.Core.Cache;

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

        Never = 0L,
        Forever = long.MaxValue - 1,
    }


    public class TimeIntervalModel : IJournalValue
    {
        public static TimeIntervalModel Never { get; } = new(TimeInterval.Never);


        public TimeInterval Interval { get; } = TimeInterval.FromParent;

        public long Ticks { get; }


        public bool IsFromFolder => Interval is TimeInterval.FromFolder;

        public bool IsFromParent => Interval is TimeInterval.FromParent;

        public bool UseCustom => Interval is TimeInterval.Custom or TimeInterval.FromFolder;


        public TimeIntervalModel(long ticks) : this(TimeInterval.Custom, ticks) { }

        public TimeIntervalModel(TimeInterval interval) : this(interval, 0L) { }

        public TimeIntervalModel(TimeIntervalEntity entity) : this((TimeInterval)entity.Interval, entity.Ticks) { }

        public TimeIntervalModel(TimeInterval interval, long ticks)
        {
            Interval = interval;
            Ticks = ticks;
        }


        internal bool TimeIsUp(DateTime time) => UseCustom ? (DateTime.UtcNow - time).Ticks > Ticks
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

        public string GetValue()
        {
            if (IsFromParent)
                return TimeInterval.FromParent.ToString();

            if (UseCustom)
                return Ticks.ToString();
            
            return Interval.ToString();
        }
    }
}