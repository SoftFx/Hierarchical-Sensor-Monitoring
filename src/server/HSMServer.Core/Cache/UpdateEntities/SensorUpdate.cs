using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;
using System.Text;

namespace HSMServer.Core.Cache.UpdateEntities
{
    /// <summary>
    /// If properties are null - there's no updates for that properties
    /// </summary>
    public record SensorUpdate : BaseNodeUpdate, IUpdateComparer<BaseSensorModel, SensorUpdate>
    {
        public SensorState? State { get; init; }

        public Integration? Integration { get; init; }

        public DateTime? EndOfMutingPeriod { get; init; }

        public List<DataPolicyUpdate> DataPolicies { get; init; }
        
        public string Compare(BaseSensorModel entity, SensorUpdate update)
        {
            var builder = new StringBuilder();

            if (entity.State != State && State is not null)
                builder.AppendLine($"State: {entity.State} -> {State}");

            if (entity.Integration != Integration && Integration is not null)
                builder.AppendLine($"Integration: {entity.Integration} -> {Integration}");

            if (entity.EndOfMuting != EndOfMutingPeriod && EndOfMutingPeriod is not null)
                builder.AppendLine($"End of muting: {entity.EndOfMuting} -> {EndOfMutingPeriod}");

            return builder.ToString();
        }
    }


    public sealed record PolicyConditionUpdate(
        PolicyOperation Operation,
        TargetValue Target,
        string Property,
        PolicyCombination Combination = PolicyCombination.And);


    public sealed class DataPolicyUpdate : IUpdateComparer<Policy, DataPolicyUpdate>
    {
        public Guid Id { get; init; }

        public List<PolicyConditionUpdate> Conditions { get; init; }

        public TimeIntervalModel Sensitivity { get; set; }
        
        public SensorStatus Status { get; init; }

        public string Template { get; init; }

        public string Icon { get; init; }


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
            var builder = new StringBuilder();

            if (entity.Icon != update.Icon)
                builder.AppendLine($"Icon: {entity.Icon} -> {update.Icon}");
            
            if (entity.Template != update.Template)
                builder.AppendLine($"Template: {entity.Template} -> {update.Template}");
            
            if (entity.Status != update.Status)
                builder.AppendLine($"Status: {entity.Status} -> {update.Status}");
            
            if (update.Conditions?.Count > 0)
                builder.AppendLine("Conditions updated.");
            
            return builder.ToString();
        }
    }
}
