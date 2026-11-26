using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Extensions;
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

        Year = 315_360_000_000_000, // 365 days
    }


    public enum NoneValues
    {
        Never,
        Forever,
    }


    public class TimeIntervalModel
    {
        public static TimeIntervalModel None { get; } = new(TimeInterval.None);


        public TimeInterval Interval { get; }

        public long Ticks { get; }

        /// <summary>
        /// The calendar preset (a month, three months, six months, a year) a <see cref="TimeInterval.FromFolder"/> model
        /// inherits from its folder (#1864); null for every other model. A calendar preset has no length in ticks, so
        /// without it the inherited model carried 0 ticks and never expired.
        /// </summary>
        public TimeInterval? FolderPreset { get; }

        /// <summary>The interval the model is evaluated by: the folder's calendar preset when it inherits one, otherwise <see cref="Interval"/>.</summary>
        public TimeInterval AppliedInterval => FolderPreset ?? Interval;


        public bool IsNone => Interval is TimeInterval.None;

        public bool IsFromFolder => Interval is TimeInterval.FromFolder;

        public bool IsFromParent => Interval is TimeInterval.FromParent;

        public bool UseTicks => AppliedInterval is TimeInterval.Ticks or TimeInterval.FromFolder;


        public TimeIntervalModel() : this(TimeInterval.FromParent) { }

        public TimeIntervalModel(long ticks) : this(ticks == long.MaxValue ? TimeInterval.None : TimeInterval.Ticks, ticks) { }

        public TimeIntervalModel(TimeInterval interval) : this(interval, 0L) { }

        public TimeIntervalModel(TimeIntervalEntity entity) : this((TimeInterval)entity.Interval, entity.Ticks, (TimeInterval?)entity.FolderPreset) { }

        public TimeIntervalModel(TimeInterval interval, long ticks) : this(interval, ticks, null) { }

        private TimeIntervalModel(TimeInterval interval, long ticks, TimeInterval? folderPreset)
        {
            Interval = interval;
            Ticks = Interval is TimeInterval.None ? long.MaxValue : ticks;

            // Only an inherited model carries a preset, and only a calendar one: anything else read from storage is ignored.
            FolderPreset = Interval is TimeInterval.FromFolder && folderPreset is { } preset && IsCalendarPreset(preset) ? preset : null;
        }


        public static bool IsCalendarPreset(TimeInterval interval) =>
            interval is TimeInterval.Month or TimeInterval.ThreeMonths or TimeInterval.SixMonths or TimeInterval.Year;


        internal bool TimeIsUp(DateTime time) =>
            UseTicks ? Ticks > 0 && (DateTime.UtcNow - time).Ticks > Ticks
                     : DateTime.UtcNow > GetShiftedTime(time);

        /// <summary>
        /// The time below which the retention pass removes a sensor's history at <paramref name="utcNow"/> under this
        /// KeepHistory policy, by <see cref="AppliedInterval"/> (a calendar preset inherited from a folder included, #1864);
        /// null when retention never removes any: None (Forever), a period of ticks that is not positive (a preset inherited
        /// before #1864 and not yet re-derived at start) or that reaches before <see cref="DateTime.MinValue"/> (Forever
        /// inherited from a folder, long.MaxValue). The retention pass and the history floor of late values and large files
        /// (#1441, #1883) both take it, so they cannot drift apart.
        /// </summary>
        internal DateTime? RetentionCutoff(DateTime utcNow) =>
            IsNone || (UseTicks && (Ticks <= 0L || Ticks >= utcNow.Ticks)) ? null : GetShiftedTime(utcNow, -1);

        public DateTime GetShiftedTime(DateTime time, int coef = 1) => AppliedInterval switch
        {
            TimeInterval.Month => time.AddMonths(coef),
            TimeInterval.ThreeMonths => time.AddMonths(3 * coef),
            TimeInterval.SixMonths => time.AddMonths(6 * coef),

            TimeInterval.Year => time.AddYears(coef),

            TimeInterval.Ticks or TimeInterval.FromFolder => time.AddTicks(Ticks * coef),
            TimeInterval.None => DateTime.MaxValue,

            _ => throw new NotImplementedException($"{Interval} is not implemented for ShiftTime function"),
        };


        // A calendar preset is inherited as itself, so the inheriting model is evaluated by the same calendar arithmetic
        // as the preset set on the node (#1864); a period of ticks is inherited as its ticks.
        public TimeIntervalModel ToFromFolderModel() =>
            IsCalendarPreset(AppliedInterval) ? new(TimeInterval.FromFolder, 0L, AppliedInterval) : new(TimeInterval.FromFolder, Ticks);

        public TimeIntervalEntity ToEntity() => new((long)Interval, Ticks) { FolderPreset = (long?)FolderPreset };

        public TimeSpan ToTimeSpan() => new(Ticks);


        public override string ToString() => FolderPreset is { } preset
            ? $"{Interval}({preset.ToDisplay()})"
            : UseTicks ? $"{Interval}({ToTimeSpan()})" : $"{Interval.ToDisplay()}";
    }
}