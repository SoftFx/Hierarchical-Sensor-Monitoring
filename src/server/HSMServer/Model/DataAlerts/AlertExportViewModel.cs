using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.DataAlerts
{
    public sealed class AlertExportViewModel
    {
        public List<string> Products { get; set; }

        public List<string> Sensors { get; set; }

        public List<ConditionExportViewModel> Conditions { get; set; }

        public string Template { get; set; }

        public string Icon { get; set; }

        public SensorStatus Status { get; set; }

        public TimeSpan? ConfirmationPeriod { get; set; }

        public string ScheduledNotificationTime { get; set; }

        public AlertRepeatMode? ScheduledRepeatMode { get; set; }

        public bool SendScheduleFirstMessage { get; set; }

        public List<string> Chats { get; set; }

        public bool IsDisabled { get; set; }


        public AlertExportViewModel() { }

        internal AlertExportViewModel(IEnumerable<PolicyExportInfo> infoList, Dictionary<Guid, string> availableChats)
        {
            Sensors = infoList.Select(u => u.FullRelativePath).OrderBy(u => u).ToList();

            var info = infoList.First();
            var policy = info.Policy;

            if (info.ProductName is not null)
                Products = new List<string>() { info.ProductName };

            Icon = policy.Icon;
            Status = policy.Status;
            Template = policy.Template;
            ConfirmationPeriod = policy.ConfirmationPeriod.HasValue ? new TimeSpan(policy.ConfirmationPeriod.Value) : null;
            IsDisabled = policy.IsDisabled;
            ScheduledNotificationTime = policy.Schedule.Time == DateTime.MinValue ? null : policy.Schedule.Time.ToDefaultFormat();
            ScheduledRepeatMode = policy.Schedule.RepeatMode; // TODO: null if None or Immediatly?
            SendScheduleFirstMessage = policy.Schedule.SendFirst;

            if (!policy.Destination.AllChats)
            {
                Chats = new();

                foreach (var (id, _) in policy.Destination.Chats)
                    if (availableChats.TryGetValue(id, out var name))
                        Chats.Add(name);
            }

            Conditions = policy.Conditions.Select(c => new ConditionExportViewModel(c)).ToList();
        }


        internal PolicyUpdate ToUpdate(Guid sensorId, Dictionary<string, Guid> availableChats) =>
            new()
            {
                Icon = Icon,
                Status = Status,
                Template = Template,
                IsDisabled = IsDisabled,
                ConfirmationPeriod = ConfirmationPeriod?.Ticks,
                Schedule = new PolicyScheduleUpdate(ScheduledNotificationTime.ParseFromDefault(), ScheduledRepeatMode, SendScheduleFirstMessage),
                Conditions = Conditions.Select(c => c.ToUpdate(sensorId)).ToList(),
                Destination = Chats is null
                    ? new PolicyDestinationUpdate(allChats: true)
                    : new PolicyDestinationUpdate(Chats.Where(availableChats.ContainsKey).ToDictionary(k => availableChats[k], v => v)),
            };
    }


    public sealed class ConditionExportViewModel
    {
        public PolicyProperty Property { get; set; }

        public PolicyOperation Operation { get; set; }

        public string Target { get; set; }


        public ConditionExportViewModel() { }

        internal ConditionExportViewModel(PolicyCondition condition)
        {
            Property = condition.Property;
            Operation = condition.Operation;
            Target = condition.Target.Type == TargetType.Const ? condition.Target.Value : null;
        }


        internal PolicyConditionUpdate ToUpdate(Guid sensorId) =>
            new(Operation,
                Property,
                Operation.IsTargetVisible()
                    ? new(TargetType.Const, Target)
                    : new(TargetType.LastValue, sensorId.ToString()));
    }
}
