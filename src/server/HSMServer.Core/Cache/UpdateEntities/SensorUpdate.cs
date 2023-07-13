using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Cache.UpdateEntities
{
    /// <summary>
    /// If properties are null - there's no updates for that properties
    /// </summary>
    public record SensorUpdate : BaseNodeUpdate
    {
        public SensorState? State { get; init; }

        public Integration? Integration { get; init; }

        public DateTime? EndOfMutingPeriod { get; init; }

        public List<DataPolicyUpdate> DataPolicies { get; init; }
    }


    public sealed class PolicyConditionUpdate : IPolicyCondition
    {
        public PolicyOperation Operation { get; set; }
        
        public TargetValue Target { get; set; }
        
        public string Property { get; set; }
        
        public PolicyCombination Combination { get; set; }
        

        public PolicyConditionUpdate(PolicyOperation operation, TargetValue target, string property, PolicyCombination combination = PolicyCombination.And)
        {
            Operation = operation;
            Target = target;
            Property = property;
            Combination = combination;
        }
    }


    public sealed class DataPolicyUpdate : IUpdateComparer<Policy, DataPolicyUpdate>, IPolicy<PolicyConditionUpdate>
    {
        public Guid Id { get; init; }

        public List<PolicyConditionUpdate> Conditions { get; init; }

        public TimeIntervalModel Sensitivity { get; set; }
        
        public SensorStatus Status { get; set; }

        public string Template { get; set; }

        public string Icon { get; set; }


        public DataPolicyUpdate(Guid id, List<PolicyConditionUpdate> conditions, TimeIntervalModel sensitivity, SensorStatus status, string template, string icon)
        {
            Id = id;
            Conditions = conditions;
            Sensitivity = sensitivity;
            Status = status;
            Template = template;
            Icon = icon;
        }

        public string Compare(Policy entity, DataPolicyUpdate update)
        {
            var oldValue = GetValue(entity);
            var newValue = GetValue(update);

            string GetValue<U>(IPolicy<U> properties) where U : IPolicyCondition
            {
                return $"{string.Join(",", properties.Conditions.Select(x => $"{x.Property} {x.Operation} {x.Target.Value}"))} {properties.Icon} {properties.Template} {(properties.Status is SensorStatus.Ok ? string.Empty : properties.Status)}";
            }

            return oldValue != newValue ? $"Old alert: {oldValue}{Environment.NewLine}New alert: {newValue}" : string.Empty;
        }
    }
}
