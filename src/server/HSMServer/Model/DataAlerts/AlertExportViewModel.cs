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
        public List<string> Sensors { get; set; }

        public List<ConditionExportViewModel> Conditions { get; set; }

        public string Template { get; set; }

        public string Icon { get; set; }

        public SensorStatus Status { get; set; }

        public TimeSpan? ConfirmationPeriod { get; set; }

        public List<string> Chats { get; set; }

        public bool IsDisabled { get; set; }


        public AlertExportViewModel() { }

        internal AlertExportViewModel(List<PolicyExportInfo> info)
        {
            Sensors = info.Select(u => u.FullRelativePath).OrderBy(u => u).ToList();

            var policy = info.First().Policy;

            Icon = policy.Icon;
            Status = policy.Status;
            Template = policy.Template;
            ConfirmationPeriod = policy.ConfirmationPeriod.HasValue ? new TimeSpan(policy.ConfirmationPeriod.Value) : null;
            IsDisabled = policy.IsDisabled;

            if (!policy.Destination.AllChats)
                Chats = policy.Destination.Chats.Values.ToList();

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
                Conditions = Conditions.Select(c => c.ToUpdate(sensorId)).ToList(),
                Destination = Chats is null
                    ? new PolicyDestinationUpdate()
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
