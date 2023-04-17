using System;
using HSMServer.Model.TreeViewModel;
using System.Collections.Generic;


namespace HSMServer.Model.ViewModel
{
    public abstract class NodeInfoBaseViewModel
    {
        private static readonly List<TimeInterval> _predefinedExpectedIntervals =
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
        
        private static readonly List<TimeInterval> _predefinedRestoreIntervals =
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


        public string Path { get; }

        public string ProductName { get; }
        
        public DateTime UpdateTime { get; set; }

        public SensorStatus Status { get; set; }
        
        public TimeIntervalViewModel ExpectedUpdateInterval { get; set; }

        public TimeIntervalViewModel SensorRestorePolicy { get; set; }


        public string EncodedId { get; set; }

        public string Description { get; set; }


        public NodeInfoBaseViewModel() { }

        internal NodeInfoBaseViewModel(NodeViewModel model)
        {
            Path = model.Path;
            ProductName = model.RootProduct.Name;
            EncodedId = model.EncodedId;
            Description = model.Description;

            ExpectedUpdateInterval = new(model.ExpectedUpdateInterval, _predefinedExpectedIntervals);
            SensorRestorePolicy = new(model.SensorRestorePolicy, _predefinedRestoreIntervals);
        }
    }
}
