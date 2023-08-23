using HSMServer.Extensions;
using HSMServer.Model.DataAlerts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.TreeViewModel
{
    public abstract class BaseNodeViewModel
    {
        internal event Func<bool> CheckJournalCount;


        public Dictionary<byte, List<DataAlertViewModelBase>> DataAlerts { get; protected set; } = new();

        public ConcurrentDictionary<string, int> AlertIcons { get; } = new();


        public TimeIntervalViewModel KeepHistory { get; protected set; }

        public TimeIntervalViewModel SelfDestroy { get; protected set; }

        public TimeIntervalViewModel TTL { get; protected set; }

        public TimeToLiveAlertViewModel TTLAlert { get; protected set; }


        public Guid Id { get; protected set; }

        public string Name { get; protected set; }

        public string Description { get; protected set; }


        public SensorStatus Status { get; protected set; }

        public DateTime UpdateTime { get; protected set; }


        public bool IsJournalEmpty => CheckJournalCount?.Invoke() ?? false;

        public string Title => Name?.Replace('\\', ' ') ?? string.Empty; //TODO remove after rename bad products

        public string Tooltip => $"{Name} {AlertTooltip} {Environment.NewLine}{(UpdateTime != DateTime.MinValue ? UpdateTime.ToDefaultFormat() : "no data")}";

        private string AlertTooltip => string.Join(',', AlertIcons.Select(x => x.Value > 1 ? $"{x.Key}x{x.Value}" : $"{x.Key}"));


        protected void RecalculateAlerts(params IEnumerable<NodeViewModel>[] collections)
        {
            AlertIcons.Clear();

            foreach (var collection in collections)
                foreach (var node in collection)
                    foreach (var (icon, count) in node.AlertIcons)
                    {
                        int UpdateCount(string _, int old) => old + count;

                        AlertIcons.AddOrUpdate(icon, count, UpdateCount);
                    }
        }
    }
}
