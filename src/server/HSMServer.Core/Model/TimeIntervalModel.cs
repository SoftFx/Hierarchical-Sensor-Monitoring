﻿using HSMDatabase.AccessManager.DatabaseEntities;
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
        ThreeMonths,
        SixMonths,
        Year,
        FromFolder = byte.MaxValue - 2,
        FromParent = byte.MaxValue - 1,
        Custom = byte.MaxValue,
    }

    public enum TimeIntervalCorrect : long
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
        public TimeIntervalCorrect Interval { get; } = TimeIntervalCorrect.FromParent;

        public long Ticks { get; }


        public bool IsNever => Interval == TimeIntervalCorrect.Never;

        public bool IsFromFolder => Interval == TimeIntervalCorrect.FromFolder;

        public bool IsFromParent => Interval == TimeIntervalCorrect.FromParent;

        public bool UseCustom => Interval is TimeIntervalCorrect.Custom or TimeIntervalCorrect.FromFolder;


        public TimeIntervalModel() { }

        public TimeIntervalModel(long ticks)
        {
            Ticks = ticks;
            Interval = TimeIntervalCorrect.Custom;
        }

        public TimeIntervalModel(TimeIntervalEntity entity) : this((TimeIntervalCorrect)entity.Interval, entity.Ticks) { }

        public TimeIntervalModel(TimeIntervalCorrect interval, long ticks)
        {
            Interval = interval;
            Ticks = ticks;
        }


        internal bool TimeIsUp(DateTime time) => 
            UseCustom ? (DateTime.UtcNow - time).Ticks > Ticks 
                      : DateTime.UtcNow > GetShiftedTime(time);

        public DateTime GetShiftedTime(DateTime time, int coef = 1) => Interval switch
        {
            TimeIntervalCorrect.OneMinute or TimeIntervalCorrect.FiveMinutes or
            TimeIntervalCorrect.TenMinutes or TimeIntervalCorrect.Hour or
            TimeIntervalCorrect.Day or TimeIntervalCorrect.Week => time.AddTicks(coef * (long)Interval),

            TimeIntervalCorrect.Month => time.AddMonths(coef),
            TimeIntervalCorrect.ThreeMonths => time.AddMonths(3 * coef),
            TimeIntervalCorrect.SixMonths => time.AddMonths(6 * coef),

            TimeIntervalCorrect.Year => time.AddYears(coef),

            TimeIntervalCorrect.Custom or TimeIntervalCorrect.FromFolder => time.AddTicks(Ticks * coef),
            TimeIntervalCorrect.Never or TimeIntervalCorrect.Forever => DateTime.MaxValue,

            _ => throw new NotImplementedException(),
        };

        internal TimeIntervalEntity ToEntity() => new((long)Interval, Ticks);
    }
}