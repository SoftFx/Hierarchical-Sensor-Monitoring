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
        Ticks = -1,
        None = 0L,

        Month = 26_784_000_000_000, // 31 days
        ThreeMonths = 80_352_000_000_000, // 31 * 3
        SixMonths = 160_704_000_000_000, // 31 * 6

        Year = 315_360_000_000_000,
    }


    public class TimeIntervalModel
    {
        public static TimeIntervalModel None { get; } = new(TimeInterval.None);


        public TimeInterval Interval { get; } = TimeInterval.FromParent;

        public long Ticks { get; }


        public bool IsFromFolder => Interval is TimeInterval.FromFolder;

        public bool IsFromParent => Interval is TimeInterval.FromParent;

        public bool UseTicks => Interval is TimeInterval.Ticks or TimeInterval.FromFolder;


        public TimeIntervalModel(long ticks) : this(TimeInterval.Ticks, ticks) { }

        public TimeIntervalModel(TimeInterval interval) : this(interval, 0L) { }

        public TimeIntervalModel(TimeIntervalEntity entity) : this((TimeInterval)entity.Interval, entity.Ticks) { }

        public TimeIntervalModel(TimeInterval interval, long ticks)
        {
            Interval = interval;
            Ticks = ticks;
        }


        internal bool TimeIsUp(DateTime time) => UseTicks ? (DateTime.UtcNow - time).Ticks > Ticks
                                                           : DateTime.UtcNow > GetShiftedTime(time);

        public DateTime GetShiftedTime(DateTime time, int coef = 1) => Interval switch
        {
            TimeInterval.Month => time.AddMonths(coef),
            TimeInterval.ThreeMonths => time.AddMonths(3 * coef),
            TimeInterval.SixMonths => time.AddMonths(6 * coef),

            TimeInterval.Year => time.AddYears(coef),

            TimeInterval.Ticks or TimeInterval.FromFolder => time.AddTicks(Ticks * coef),
            TimeInterval.None => DateTime.MaxValue,

            _ => throw new NotImplementedException(),
        };

        public TimeIntervalEntity ToEntity() => new((long)Interval, Ticks);
    }
}