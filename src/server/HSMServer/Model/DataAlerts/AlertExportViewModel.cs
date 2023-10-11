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

        public List<string> Chats { get; set; }

        public bool IsDisabled { get; set; }


        public AlertExportViewModel() { }

        internal AlertExportViewModel(PolicyGroup group)
        {
            Sensors = group.Policies.Select(p => p.Value.Sensor.DisplayName).OrderBy(u => u).ToList();

            var policy = group.Policies.First().Value;

            Icon = policy.Icon;
            Status = policy.Status;
            Template = policy.Template;
            IsDisabled = policy.IsDisabled;

            if (!policy.Destination.AllChats)
                Chats = policy.Destination.Chats.Values.ToList();

            Conditions = policy.Conditions.Select(c => new ConditionExportViewModel(c)).ToList();
        }


        internal Dictionary<Guid, PolicyUpdate> ToUpdates(Dictionary<string, Guid> availableSensors, Dictionary<string, Guid> availableChats)
        {
            var result = new Dictionary<Guid, PolicyUpdate>(Sensors.Count);

            foreach (var sensorName in Sensors)
                if (availableSensors.TryGetValue(sensorName, out var sensorId) && !result.ContainsKey(sensorId))
                {
                    var policyUpdate = new PolicyUpdate()
                    {
                        Icon = Icon,
                        Status = Status,
                        Template = Template,
                        IsDisabled = IsDisabled,
                        Conditions = Conditions.Select(c => c.ToUpdate(sensorId)).ToList(),
                        Destination = Chats is null
                            ? new PolicyDestinationUpdate()
                            : new PolicyDestinationUpdate(Chats.Where(availableChats.ContainsKey).ToDictionary(k => availableChats[k], v => v)),
                    };

                    result.Add(sensorId, policyUpdate);
                }

            return result;
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


        internal PolicyConditionUpdate ToUpdate(Guid sensorId) =>
            new(Operation,
                Property,
                Operation.IsTargetVisible()
                    ? new(TargetType.Const, Target)
                    : new(TargetType.LastValue, sensorId.ToString()));
    }
}
