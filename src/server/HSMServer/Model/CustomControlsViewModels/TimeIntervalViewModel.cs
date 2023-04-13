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
            new(TimeInterval.ToCore(folderInterval != null), GetCustomTicks(folderInterval));

        internal TimeIntervalModel ToFolderModel() =>
            new(CoreTimeInterval.FromFolder, TimeInterval.ToCustomTicks(CustomTimeInterval));

        internal TimeIntervalEntity ToEntity() => new((byte)TimeInterval.ToCore(), GetCustomTicks());


        private void SetInterval(CoreTimeInterval interval, long customPeriod)
        {
            TimeInterval = interval.ToServer(customPeriod);
            CustomTimeInterval = customPeriod.TicksToString();
        }

        private long GetCustomTicks(TimeIntervalViewModel folderInterval = null)
        {
            if (TimeInterval.IsCustom() && CustomTimeInterval.TryParse(out var ticks))
                return ticks;

            if (TimeInterval.IsParent() && folderInterval != null)
                return folderInterval.TimeInterval.ToCustomTicks(folderInterval.CustomTimeInterval);

            return 0L;
        }

        private static List<SelectListItem> GetIntrevalItems(List<TimeInterval> intervals)
        {
            return intervals.Select(u => new SelectListItem(u.GetDisplayName(), $"{u}")).ToList();
        }
    }
}