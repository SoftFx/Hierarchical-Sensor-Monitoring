using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;

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


    public sealed record PolicyConditionUpdate(
        PolicyCombination Combination,
        PolicyOperation Operation,
        TargetValue Target,
        string Property);


    public sealed record DataPolicyUpdate(
        Guid Id,
        List<PolicyConditionUpdate> Conditions,
        SensorStatus Status,
        string Template,
        string Icon
    );
}
