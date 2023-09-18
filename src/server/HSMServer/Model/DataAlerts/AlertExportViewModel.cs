using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.DataAlerts
{
    internal sealed class AlertExportViewModel
    {
        internal List<string> Sensors { get; }

        internal List<ConditionExportViewModel> Conditions { get; }

        internal string Template { get; }

        internal string Icon { get; }

        internal SensorStatus Status { get; }

        internal List<string> Destination { get; }

        internal bool IsDisabled { get; }


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


    internal sealed class ConditionExportViewModel
    {
        internal PolicyProperty Property { get; }

        internal PolicyOperation Operation { get; }

        internal string Target { get; }


        internal ConditionExportViewModel(PolicyCondition condition)
        {
            Property = condition.Property;
            Operation = condition.Operation;
            Target = condition.Target.Type == TargetType.Const ? condition.Target.Value : null;
        }
    }
}
