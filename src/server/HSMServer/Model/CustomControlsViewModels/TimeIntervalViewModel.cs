using HSMCommon.Extensions;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using CoreTimeInterval = HSMServer.Core.Model.TimeInterval;

namespace HSMServer.Model
{
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


        public TimeInterval? Interval { get; set; }

        public string CustomTimeInterval { get; set; }

        internal TimeInterval TimeInterval => Interval ?? default;


        // public constructor without parameters for post actions
        public TimeIntervalViewModel() { }

        internal TimeIntervalViewModel(TimeIntervalModel model, Func<TimeIntervalViewModel> getParentValue, Func<bool> hasFolder)
        {
            _getParentValue = getParentValue;
            _hasFolder = hasFolder;

            FromModel(model);
        }

        internal TimeIntervalViewModel(List<TimeInterval> intervals, bool useCutomTemplate = true)
        {
            IntervalItems = GetIntrevalItems(intervals);
            UseCustomInputTemplate = useCutomTemplate;

            if (!HasParentValue)
                IntervalItems.RemoveAt(0);
        }

        internal TimeIntervalViewModel(TimeIntervalViewModel model, List<TimeInterval> intervals) : this(intervals)
        {
            _getParentValue = model._getParentValue;
            _hasFolder = model._hasFolder;

            Interval = model.Interval;
            CustomTimeInterval = model.CustomTimeInterval;
        }

        internal TimeIntervalViewModel(TimeIntervalEntity entity, List<TimeInterval> intervals) : this( intervals)
        {
            FromModel(new TimeIntervalModel(entity));
        }


        //internal void Update(TimeIntervalModel model) =>
        //    Update(model?.Interval ?? CoreTimeInterval.FromParent, model?.Ticks ?? 0L);


        //internal TimeIntervalModel ToModel(TimeIntervalViewModel folderInterval = null) =>
        //    new(TimeInterval.ToCore(folderInterval != null), GetCustomTicks(folderInterval));

        //internal TimeIntervalModel ToFolderModel() =>
        //    new(CoreTimeInterval.FromFolder, TimeInterval.ToCustomTicks(CustomTimeInterval));

        internal TimeIntervalEntity ToEntity() => ToModel().ToEntity();


        internal TimeIntervalModel ToModel()
        {
            if ((Interval >= TimeInterval.OneMinute && Interval <= TimeInterval.Month) || Interval is TimeInterval.Custom)
                return new TimeIntervalModel(Interval.Value.ToCustomTicks(CustomTimeInterval));
            else if (Interval is TimeInterval.Forever or TimeInterval.None)
                return TimeIntervalModel.None;
            else if (Interval >= TimeInterval.Month && Interval <= TimeInterval.Year)
                return new TimeIntervalModel(Interval switch
                {
                    TimeInterval.Month => CoreTimeInterval.Month,
                    TimeInterval.ThreeMonths => CoreTimeInterval.ThreeMonths,
                    TimeInterval.SixMonths => CoreTimeInterval.SixMonths,
                    TimeInterval.Year => CoreTimeInterval.Year,
                    _ => throw new NotImplementedException(),
                });
            else
            {
                if (HasFolder)
                {
                    var parentValue = _getParentValue?.Invoke();

                    return new TimeIntervalModel(CoreTimeInterval.FromFolder, parentValue.Interval.Value.ToCustomTicks(parentValue.CustomTimeInterval));
                }
                else
                    return new TimeIntervalModel(CoreTimeInterval.FromParent);
            }
        }

        internal TimeIntervalViewModel FromModel(TimeIntervalModel model)
        {
            if (model is null)
                return this;

            if (!model.UseTicks)
            {
                Interval = model.Interval switch
                {
                    CoreTimeInterval.Month => TimeInterval.Month,
                    CoreTimeInterval.ThreeMonths => TimeInterval.ThreeMonths,
                    CoreTimeInterval.SixMonths => TimeInterval.SixMonths,
                    CoreTimeInterval.Year => TimeInterval.Year,

                    CoreTimeInterval.FromParent => TimeInterval.FromParent,
                    CoreTimeInterval.None => TimeInterval.None,

                    _ => throw new NotImplementedException(),
                };
            }
            else
            {
                if (model.Interval is CoreTimeInterval.FromFolder)
                    Interval = TimeInterval.FromParent;
                else
                    Interval = model.Ticks switch
                    {
                        600_000_000L => TimeInterval.OneMinute,
                        3_000_000_000L => TimeInterval.FiveMinutes,
                        6_000_000_000L => TimeInterval.TenMinutes,
                        18_000_000_000L => TimeInterval.ThirtyMinutes,

                        36_000_000_000L => TimeInterval.Hour,
                        144_000_000_000L => TimeInterval.FourHours,
                        288_000_000_000L => TimeInterval.EightHours,
                        576_000_000_000L => TimeInterval.SixteenHours,

                        864_000_000_000L => TimeInterval.Day,
                        1_296_000_000_000L => TimeInterval.ThirtySixHours,
                        2_160_000_000_000L => TimeInterval.SixtyHours,
                        6_048_000_000_000L => TimeInterval.Week,
                        _ => TimeInterval.Custom,
                    };

                if (Interval is TimeInterval.Custom)
                    CustomTimeInterval = $"{new TimeSpan(model.Ticks)}";
            }

            return this;
        }

        //internal TimeIntervalViewModel ResaveCustomTicks(TimeIntervalViewModel interval) //????? need check
        //{
        //    interval.CustomTimeInterval = new TimeSpan(GetCustomTicks(interval)).ToString();

        //    return interval;
        //}

        //private void Update(CoreTimeInterval interval, long customPeriod)
        //{
        //    Interval = interval.ToServer(customPeriod);
        //    CustomTimeInterval = customPeriod.TicksToString();
        //}

        //private long GetCustomTicks(TimeIntervalViewModel folderInterval = null)
        //{
        //    if (TimeInterval.IsCustom() && CustomTimeInterval.TryParse(out var ticks))
        //        return ticks;

        //    if (TimeInterval.IsForever())
        //        return TimeInterval.ToCustomTicks(null);

        //    if (TimeInterval.IsParent() && folderInterval != null)
        //        return folderInterval.TimeInterval.ToCustomTicks(folderInterval.CustomTimeInterval);

        //    return 0L;
        //}

        private static List<SelectListItem> GetIntrevalItems(List<TimeInterval> intervals)
        {
            return intervals.Select(u => new SelectListItem(u.GetDisplayName(), $"{u}")).ToList();
        }
    }
}