using HSMServer.Core.Model;
using System;
using System.ComponentModel.DataAnnotations;
using CoreTimeInterval = HSMServer.Core.Model.TimeInterval;

namespace HSMServer.Model
{
    public enum TimeInterval
    {
        [Display(Name = "Never")]
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

        internal TimeIntervalViewModel(TimeIntervalModel model)
        {
            Update(model);
        }


        internal void Update(TimeIntervalModel model)
        {
            var customIntervalTicks = model?.CustomPeriod;

            TimeInterval = SetTimeInterval(model?.TimeInterval, customIntervalTicks);
            CustomTimeInterval = new TimeSpan(customIntervalTicks ?? 0).ToString();
        }

        internal TimeIntervalModel ToModel() =>
            new()
            {
                TimeInterval = GetIntervalOption(),
                CustomPeriod = GetCustomIntervalTicks(),
            };

        private CoreTimeInterval GetIntervalOption() =>
            TimeInterval switch
            {
                TimeInterval.TenMinutes => CoreTimeInterval.TenMinutes,
                TimeInterval.Hour => CoreTimeInterval.Hour,
                TimeInterval.Day => CoreTimeInterval.Day,
                TimeInterval.Week => CoreTimeInterval.Week,
                TimeInterval.Month => CoreTimeInterval.Month,
                TimeInterval.Custom => CoreTimeInterval.Custom,
                _ => CoreTimeInterval.Custom,
            };

        private long GetCustomIntervalTicks()
        {
            if (TimeInterval == TimeInterval.Custom && TimeSpan.TryParse(CustomTimeInterval, out var timeInterval))
                return timeInterval.Ticks;

            return 0;
        }

        private static TimeInterval SetTimeInterval(CoreTimeInterval? interval, long? customIntervalTicks)
        {
            if (interval is null)
                return TimeInterval.None;

            return interval.Value switch
            {
                CoreTimeInterval.TenMinutes => TimeInterval.TenMinutes,
                CoreTimeInterval.Hour => TimeInterval.Hour,
                CoreTimeInterval.Day => TimeInterval.Day,
                CoreTimeInterval.Week => TimeInterval.Week,
                CoreTimeInterval.Month => TimeInterval.Month,
                CoreTimeInterval.Custom => (customIntervalTicks ?? 0) == 0 ? TimeInterval.None : TimeInterval.Custom,
                _ => TimeInterval.None,
            };
        }
    }
}
