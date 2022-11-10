using HSMServer.Core.Model;
using HSMServer.Extensions;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.TreeViewModels
{
    public abstract class NodeViewModel
    {
        internal List<TimeInterval> ExpectedUpdateTimeInternals { get; } =
            new()
            {
                TimeInterval.None,
                TimeInterval.TenMinutes,
                TimeInterval.Hour,
                TimeInterval.Day,
                TimeInterval.Week,
                TimeInterval.Month,
                TimeInterval.Custom
            };


        public string EncodedId { get; }

        public string Name { get; protected set; }

        public DateTime UpdateTime { get; protected set; }

        public SensorStatus Status { get; protected set; }

        public NodeViewModel Parent { get; internal set; }

        public string Path { get; internal set; }


        public string Tooltip =>
            $"{Name}{Environment.NewLine}{(UpdateTime != DateTime.MinValue ? UpdateTime.ToDefaultFormat() : "no data")}";

        public string Title => Name?.Replace('\\', ' ') ?? string.Empty;


        internal NodeViewModel(string encodedId)
        {
            EncodedId = encodedId;
        }
    }
}
