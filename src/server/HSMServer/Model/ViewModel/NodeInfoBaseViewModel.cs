﻿using HSMServer.Model.TreeViewModel;
using System.Collections.Generic;

namespace HSMServer.Model.ViewModel
{
    public abstract class NodeInfoBaseViewModel
    {
        protected static readonly List<TimeInterval> _predefinedIntervals =
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


        public string Path { get; }

        public string ProductName { get; }

        public string EncodedId { get; set; }
        
        public string Description { get; set; }

        public TimeIntervalViewModel ExpectedUpdateInterval { get; set; }


        public NodeInfoBaseViewModel() { }

        internal NodeInfoBaseViewModel(NodeViewModel model)
        {
            Path = model.Path;
            ProductName = model.RootProduct.Name;
            EncodedId = model.EncodedId;
            Description = model.Description;

            ExpectedUpdateInterval = new(model.ExpectedUpdateInterval.ToModel(), _predefinedIntervals);
        }
    }
}
