using HSMServer.Core.Model;
using System;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model
{
    public enum TimeInterval
    {
        [Display(Name = "")]
        None,
        [Display(Name = "10 minutes")]
        TenMinutes,
        [Display(Name = "1 hour")]
        Hour,
        [Display(Name = "1 day")]
        Day,
        [Display(Name = "1 week")]
        Week,
        [Display(Name = "1 month")]
        Month,
        Custom,
    }


    public record TimeIntervalViewModel
    {
        public TimeInterval TimeInterval { get; set; }

        public string CustomTimeInterval { get; set; }


        // public constructor without parameters for post actions
        public TimeIntervalViewModel() { }

        internal TimeIntervalViewModel(byte? timeInterval, long? customIntervalTicks)
        {
            Update(timeInterval, customIntervalTicks);
        }


        internal void Update(byte? timeInterval, long? customIntervalTicks)
        {
            TimeInterval = SetTimeInterval(timeInterval, customIntervalTicks);
            CustomTimeInterval = new TimeSpan(customIntervalTicks ?? 0).ToString();
        }

        internal byte GetIntervalOption() =>
            (byte)(TimeInterval switch
            {
                TimeInterval.TenMinutes => Interval.TenMinutes,
                TimeInterval.Hour => Interval.Hour,
                TimeInterval.Day => Interval.Day,
                TimeInterval.Week => Interval.Week,
                TimeInterval.Month => Interval.Month,
                TimeInterval.Custom => Interval.Custom,
                _ => Interval.Custom,
            });

        internal long GetCustomIntervalTicks()
        {
            if (TimeInterval == TimeInterval.Custom && TimeSpan.TryParse(CustomTimeInterval, out var timeInterval))
                return timeInterval.Ticks;

            return 0;
        }

        private static TimeInterval SetTimeInterval(byte? interval, long? customIntervalTicks)
        {
            if (interval is null)
                return TimeInterval.None;

            return (Interval)interval.Value switch
            {
                Interval.TenMinutes => TimeInterval.TenMinutes,
                Interval.Hour => TimeInterval.Hour,
                Interval.Day => TimeInterval.Day,
                Interval.Week => TimeInterval.Week,
                Interval.Month => TimeInterval.Month,
                Interval.Custom => (customIntervalTicks ?? 0) == 0 ? TimeInterval.None : TimeInterval.Custom,
                _ => TimeInterval.None,
            };
        }
    }
}
