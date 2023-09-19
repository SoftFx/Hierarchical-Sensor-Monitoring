using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
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

        public List<string> Destination { get; set; }

        public bool IsDisabled { get; set; }


        public AlertExportViewModel() { }

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
    }
}
