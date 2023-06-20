using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Model.DataAlerts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HSMServer.Model.TreeViewModel
{
    public abstract class BaseNodeViewModel
    {
        public Dictionary<SensorType, List<DataAlertViewModel>> DataAlerts { get; protected set; } = new();

        public ConcurrentDictionary<string, int> AlertIcons { get; } = new();


        public TimeIntervalViewModel ExpectedUpdateInterval { get; protected set; }

        public TimeIntervalViewModel SensorRestorePolicy { get; protected set; }

        public TimeIntervalViewModel SavedHistoryPeriod { get; protected set; }

        public TimeIntervalViewModel SelfDestroyPeriod { get; protected set; }


        public Guid Id { get; protected set; }

        public string Name { get; protected set; }

        public string Description { get; protected set; }


        public SensorStatus Status { get; protected set; }

        public DateTime UpdateTime { get; protected set; }


        public string Title => Name?.Replace('\\', ' ') ?? string.Empty; //TODO remove after rename bad products

        public string Tooltip => $"{Name}{Environment.NewLine}{(UpdateTime != DateTime.MinValue ? UpdateTime.ToDefaultFormat() : "no data")}";


        protected void RecalculateAlerts(params IEnumerable<NodeViewModel>[] collections)
        {
            AlertIcons.Clear();

            foreach (var collection in collections)
                foreach (var node in collection)
                    foreach (var (icon, count) in node.AlertIcons)
                    {
                        if (!AlertIcons.ContainsKey(icon))
                            AlertIcons.TryAdd(icon, 0);

                        AlertIcons[icon] += count;
                    }
        }
    }
}
