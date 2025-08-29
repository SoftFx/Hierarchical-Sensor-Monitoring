using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.TaskResult;
using HSMSensorDataObjects;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.TableOfChanges;
using SensorStatus = HSMServer.Core.Model.SensorStatus;


namespace HSMServer.Core.Cache.UpdateEntities
{
    /// <summary>
    /// If properties are null - there's no updates for that properties
    /// </summary>
    public record SensorUpdate : BaseNodeUpdate, IUpdateRequest
    {
        public List<PolicyUpdate> Policies { get; init; }


        public StatisticsOptions? Statistics { get; init; }

        public DateTime? EndOfMutingPeriod { get; init; }

        public Integration? Integration { get; init; }

        public bool? AggregateValues { get; init; }

        public SensorState? State { get; init; }

        public Unit? SelectedUnit { get; init; }

        public bool? IsSingleton { get; init; }

        public bool? IsHideEnabled { get; init; }
    
        public int? MaxCommentHideSize { get; init; }
        

        public DefaultAlertsOptions DefaultAlertsOptions { get; init; }

        public List<EnumOption> EnumOptions { get; init; }

        [SetsRequiredMembers]
        public SensorUpdate() : base() { }
    }


    public sealed record PolicyUpdate
    {
        public List<PolicyConditionUpdate> Conditions { get; init; }

        public PolicyDestinationUpdate Destination { get; init; }

        public PolicyScheduleUpdate Schedule { get; init; }

        public long? ConfirmationPeriod { get; init; }


        public Guid Id { get; init; }

        public SensorStatus Status { get; init; }

        public string Template { get; init; }

        public bool IsDisabled { get; init; }

        public string Icon { get; init; }


        public InitiatorInfo Initiator { get; init; }

        public bool IsParentRequest { get; init; }

        public Guid? TemplateId { get; init; }

        public PolicyUpdate() { }

        public PolicyUpdate (Policy policy, InitiatorInfo initiator = null)
        {
            Conditions = policy.Conditions.Select(x => new PolicyConditionUpdate(x.Operation, x.Property, x.Target, x.Combination)).ToList();
            Destination = new PolicyDestinationUpdate(policy.Destination);
            Schedule = new PolicyScheduleUpdate() { RepeatMode = policy.Schedule.RepeatMode, InstantSend = policy.Schedule.InstantSend, Time = policy.Schedule.Time };
            ConfirmationPeriod = policy.ConfirmationPeriod;
            Id = policy.Id;
            Status = policy.Status;
            Template = policy.Template;
            IsDisabled = policy.IsDisabled;
            Icon = policy.Icon;
            TemplateId = policy.TemplateId;
            Initiator = initiator;
        }

    }


    public sealed record PolicyConditionUpdate
    {
        public required PolicyOperation Operation { get; init; }

        public required PolicyProperty Property { get; init; }

        public required TargetValue Target { get; init; }

        public PolicyCombination Combination { get; init; }


        public PolicyConditionUpdate() { }

        [SetsRequiredMembers]
        public PolicyConditionUpdate(PolicyOperation operation, PolicyProperty property, TargetValue target, PolicyCombination combination = PolicyCombination.And)
        {
            Operation = operation;
            Property = property;
            Target = target;
            Combination = combination;
        }
    }


    public sealed record PolicyDestinationUpdate
    {
        public Dictionary<Guid, string> Chats { get; } = [];

        public PolicyDestinationMode? Mode { get; }


        public PolicyDestinationUpdate(PolicyDestinationMode? mode = null)
        {
            Mode = mode;
        }

        public PolicyDestinationUpdate(Dictionary<Guid, string> chats, PolicyDestinationMode? mode = null) : this(mode)
        {
            Chats = chats;
        }

        public PolicyDestinationUpdate(PolicyDestination destination) : this(destination.Mode)
        {
            Chats = new Dictionary<Guid, string>(destination.Chats);
        }
    }


    public sealed record PolicyScheduleUpdate
    {
        public AlertRepeatMode? RepeatMode { get; init; }

        public bool? InstantSend { get; init; }

        public DateTime? Time { get; init; }

        public PolicyScheduleUpdate() { }

        public PolicyScheduleUpdate(PolicySchedule schedule)
        {
            RepeatMode = schedule.RepeatMode;
            InstantSend = schedule.InstantSend;
            Time = schedule.Time;
        }
    }
}