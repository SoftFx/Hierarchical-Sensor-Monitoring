﻿using HSMServer.Core.Model;
using HSMServer.Extensions;
using System;

namespace HSMServer.Model.TreeViewModels
{
    public enum SensorStatusWeb
    {
        OffTime,
        Ok,
        Warning,
        Error,
    }


    public abstract class NodeViewModel
    {
        public string EncodedId { get; }

        public string Name { get; protected set; }

        public DateTime UpdateTime { get; protected set; }

        public SensorStatusWeb Status { get; protected set; }

        public string Product { get; protected set; }

        public string Path { get; protected set; }

        public bool IsOwnExpectedUpdateInterval { get; protected set; }

        public NodeViewModel Parent { get; internal set; }

        public TimeIntervalViewModel ExpectedUpdateInterval { get; set; } = new();


        public string Tooltip =>
            $"{Name}{Environment.NewLine}{(UpdateTime != DateTime.MinValue ? UpdateTime.ToDefaultFormat() : "no data")}";

        public string Title => Name?.Replace('\\', ' ') ?? string.Empty;


        internal NodeViewModel(string encodedId)
        {
            EncodedId = encodedId;
        }

        protected void Update(NodeBaseModel model)
        {
            Name = model.DisplayName;

            ExpectedUpdateInterval.Update(model.UsedExpectedUpdateInterval?.ToTimeInterval());
            IsOwnExpectedUpdateInterval = model.ExpectedUpdateInterval != null || model.ParentProduct == null;
        }
    }
}
