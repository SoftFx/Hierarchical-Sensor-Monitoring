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

        internal TimeIntervalViewModel(TimeIntervalEntity entity, List<TimeInterval> intervals) : this(intervals)
        {
            FromModel(new TimeIntervalModel(entity));
        }


        internal TimeIntervalEntity ToEntity() => ToModel().ToEntity();


        internal TimeIntervalModel ToModel()
        {
            if (TimeInterval.IsStatic() || TimeInterval.IsCustom())
                return new TimeIntervalModel(Interval.Value.ToCustomTicks(CustomTimeInterval));
            else if (TimeInterval.IsUnset())
                return TimeIntervalModel.None;
            else if (TimeInterval.IsDynamic())
                return new TimeIntervalModel(TimeInterval.ToStaticCore());
            else if (HasFolder)
            {
                var parentValue = _getParentValue?.Invoke();

                return new TimeIntervalModel(CoreTimeInterval.FromFolder, parentValue.Interval.Value.ToCustomTicks(parentValue.CustomTimeInterval));
            }
            else
                return new TimeIntervalModel(CoreTimeInterval.FromParent);
        }

        internal TimeIntervalViewModel FromModel(TimeIntervalModel model)
        {
            if (model.IsFromFolder)
                Interval = TimeInterval.FromParent;
            else if (!model.UseTicks) //dynamic to dynamic
                Interval = model.Interval.ToStaticServer();
            else if (TimeInterval.IsDefined(model.Ticks)) //const ticks to enum
                Interval = (TimeInterval)model.Ticks;
            else
            {
                Interval = TimeInterval.Custom;
                CustomTimeInterval = $"{new TimeSpan(model.Ticks)}";
            }

            return this;
        }

        private static List<SelectListItem> GetIntrevalItems(List<TimeInterval> intervals)
        {
            return intervals.Select(u => new SelectListItem(u.GetDisplayName(), $"{u}")).ToList();
        }
    }
}