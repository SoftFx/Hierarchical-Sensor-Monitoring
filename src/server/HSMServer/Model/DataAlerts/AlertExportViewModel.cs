using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.DataAlerts
{
    public sealed class AlertExportViewModel
    {
        public List<string> Sensors { get; }

        public List<ConditionExportViewModel> Conditions { get; }

        public string Template { get; }

        public string Icon { get; }

        public SensorStatus Status { get; }

        public List<string> Destination { get; }

        public bool IsDisabled { get; }


        internal AlertExportViewModel(PolicyGroup group)
        {
            Sensors = group.Policies.Select(p => p.Value.Sensor.DisplayName).ToList();

            var policy = group.Policies.First().Value;

            Icon = policy.Icon;
            Status = policy.Status;
            Template = policy.Template;
            IsDisabled = policy.IsDisabled;

            if (!policy.Destination.AllChats)
                Destination = policy.Destination.Chats.Values.ToList();

            Conditions = policy.Conditions.Select(c => new ConditionExportViewModel(c)).ToList();
        }
    }


    public sealed class ConditionExportViewModel
    {
        public PolicyProperty Property { get; }

        public PolicyOperation Operation { get; }

        public string Target { get; }


        internal ConditionExportViewModel(PolicyCondition condition)
        {
            Property = condition.Property;
            Operation = condition.Operation;
            Target = condition.Target.Type == TargetType.Const ? condition.Target.Value : null;
        }
    }
}
