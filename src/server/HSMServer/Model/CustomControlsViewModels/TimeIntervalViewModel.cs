using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using CoreTimeInterval = HSMServer.Core.Model.TimeInterval;

namespace HSMServer.Model
{
    public enum TimeInterval
    {
        [Display(Name = "From parent")]
        FromParent,
        [Display(Name = "Never")]
        None,
        [Display(Name = "1 minute")]
        OneMinute,
        [Display(Name = "5 minutes")]
        FiveMinutes,
        [Display(Name = "10 minutes")]
        TenMinutes,
        [Display(Name = "30 minutes")]
        ThirtyMinutes,
        [Display(Name = "1 hour")]
        Hour,
        [Display(Name = "4 hours")]
        FourHours,
        [Display(Name = "8 hours")]
        EightHours,
        [Display(Name = "16 hours")]
        SixteenHours,
        [Display(Name = "1 day")]
        Day,
        [Display(Name = "1 day 12 hours")]
        ThirtySixHours,
        [Display(Name = "2 days 12 hours")]
        SixtyHours,
        [Display(Name = "1 week")]
        Week,
        [Display(Name = "1 month")]
        Month,
        Forever,
        Custom,
    }


    public record TimeIntervalViewModel
    {
        public const string CustomTemplate = "dd.HH:mm:ss";

        private readonly Func<TimeIntervalViewModel> _getParentValue;
        private readonly Func<bool> _hasFolder;

        private static long _id = 0L;


        public List<SelectListItem> IntervalItems { get; }

        public string Id { get; } = $"{_id++}";

        public bool UseCustomInputTemplate { get; }


        private bool HasParentValue => _getParentValue?.Invoke() is not null || HasFolder;

        private bool HasFolder => _hasFolder?.Invoke() ?? false;

        private string UsedInterval => TimeInterval switch
        {
            TimeInterval.Custom => CustomTimeInterval.ToTableView(),
            TimeInterval.FromParent => HasParentValue ? _getParentValue?.Invoke().UsedInterval : TimeInterval.GetDisplayName(),
            _ => TimeInterval.GetDisplayName()
        };

        public string DisplayInterval => TimeInterval.IsParent() ? $"From parent ({UsedInterval})" : UsedInterval;


        public TimeInterval TimeInterval { get; set; }

        public string CustomTimeInterval { get; set; }


        // public constructor without parameters for post actions
        public TimeIntervalViewModel() { }

        internal TimeIntervalViewModel(TimeIntervalModel model, Func<TimeIntervalViewModel> getParentValue, Func<bool> hasFolder)
        {
            _getParentValue = getParentValue;
            _hasFolder = hasFolder;

            Update(model);
        }

        internal TimeIntervalViewModel(List<TimeInterval> intervals, bool useCutomTemplate = true)
        {
            IntervalItems = GetIntrevalItems(intervals);
            UseCustomInputTemplate = useCutomTemplate;
        }

        internal TimeIntervalViewModel(TimeIntervalViewModel model, List<TimeInterval> intervals) : this(intervals)
        {
            _getParentValue = model._getParentValue;
            _hasFolder = model._hasFolder;

            TimeInterval = model.TimeInterval;
            CustomTimeInterval = model.CustomTimeInterval;

            if (!HasParentValue)
                IntervalItems.RemoveAt(0);
        }

        internal TimeIntervalViewModel(TimeIntervalEntity entity, List<TimeInterval> intervals) : this(intervals)
        {
            SetInterval((CoreTimeInterval)entity.Interval, entity.CustomPeriod);

            if (!HasParentValue)
                IntervalItems.RemoveAt(0);
        }


        internal void Update(TimeIntervalModel model) =>
            SetInterval(model?.TimeInterval ?? CoreTimeInterval.FromParent, model?.CustomPeriod ?? 0L);

        internal TimeIntervalModel ToModel(TimeIntervalViewModel folderInterval = null) =>
            new(GetInterval(folderInterval != null), GetCustomTicks(folderInterval));

        internal TimeIntervalEntity ToEntity() => new((byte)GetInterval(), GetCustomTicks());

        internal TimeIntervalModel ToFolderModel() =>
            new(CoreTimeInterval.FromFolder, TimeInterval.ToCustomTicks(CustomTimeInterval));


        private void SetInterval(CoreTimeInterval interval, long customPeriod)
        {
            TimeInterval = SetTimeInterval(interval, customPeriod);
            CustomTimeInterval = TimeSpanValue.TicksToString(customPeriod);
        }

        private CoreTimeInterval GetInterval(bool parentIsFolder = false) =>
            TimeInterval switch
            {
                TimeInterval.OneMinute => CoreTimeInterval.OneMinute,
                TimeInterval.FiveMinutes => CoreTimeInterval.FiveMinutes,
                TimeInterval.TenMinutes => CoreTimeInterval.TenMinutes,
                TimeInterval.Hour => CoreTimeInterval.Hour,
                TimeInterval.Day => CoreTimeInterval.Day,
                TimeInterval.Week => CoreTimeInterval.Week,
                TimeInterval.Month => CoreTimeInterval.Month,
                TimeInterval.FromParent => parentIsFolder ? CoreTimeInterval.FromFolder : CoreTimeInterval.FromParent,
                _ => CoreTimeInterval.Custom,
            };

        private long GetCustomTicks(TimeIntervalViewModel folderInterval = null)
        {
            if (TimeInterval == TimeInterval.Custom && TimeSpanValue.TryParse(CustomTimeInterval, out var ticks))
                return ticks;

            if (TimeInterval == TimeInterval.FromParent && folderInterval != null)
                return folderInterval.TimeInterval.ToCustomTicks(folderInterval.CustomTimeInterval);

            return 0L;
        }

        private static TimeInterval SetTimeInterval(CoreTimeInterval interval, long ticks) =>
            interval switch
            {
                CoreTimeInterval.OneMinute => TimeInterval.OneMinute,
                CoreTimeInterval.FiveMinutes => TimeInterval.FiveMinutes,
                CoreTimeInterval.TenMinutes => TimeInterval.TenMinutes,
                CoreTimeInterval.Hour => TimeInterval.Hour,
                CoreTimeInterval.Day => TimeInterval.Day,
                CoreTimeInterval.Week => TimeInterval.Week,
                CoreTimeInterval.Month => TimeInterval.Month,
                CoreTimeInterval.FromFolder or CoreTimeInterval.FromParent => TimeInterval.FromParent,
                CoreTimeInterval.Custom => ticks == 0L ? TimeInterval.None : TimeInterval.Custom,
                _ => TimeInterval.None,
            };

        private static List<SelectListItem> GetIntrevalItems(List<TimeInterval> intervals)
        {
            return intervals.Select(u => new SelectListItem(u.GetDisplayName(), $"{u}")).ToList();
        }
    }


    public static class PredefinedTimeIntervals
    {
        public static List<TimeInterval> ExpectedUpdatePolicy { get; } =
            new()
            {
                TimeInterval.FromParent,
                TimeInterval.None,
                TimeInterval.TenMinutes,
                TimeInterval.Hour,
                TimeInterval.Day,
                TimeInterval.Week,
                TimeInterval.Month,
                TimeInterval.Custom
            };

        public static List<TimeInterval> RestorePolicy { get; } =
            new()
            {
                TimeInterval.FromParent,
                TimeInterval.None,
                TimeInterval.OneMinute,
                TimeInterval.FiveMinutes,
                TimeInterval.TenMinutes,
                TimeInterval.Hour,
                TimeInterval.Day,
                TimeInterval.Custom
            };

        public static List<TimeInterval> IgnoreNotifications { get; } =
            new()
            {
                TimeInterval.FiveMinutes,
                TimeInterval.TenMinutes,
                TimeInterval.ThirtyMinutes,
                TimeInterval.FourHours,
                TimeInterval.EightHours,
                TimeInterval.SixteenHours,
                TimeInterval.ThirtySixHours,
                TimeInterval.SixtyHours,
                TimeInterval.Forever,
                TimeInterval.Custom
            };
    }
}