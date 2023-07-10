using HSMCommon.Extensions;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace HSMServer.Model.DataAlerts
{
    public class DataAlertViewModel
    {
        public List<AlertCondition> Conditions { get; set; } = new();

        public List<AlertAction> Actions { get; set; } = new();



        public PolicyOperation Operation { get; set; }

        public SensorStatus Status { get; set; }

        public string Property { get; set; }

        [Required]
        public string Comment { get; set; }

        public Guid EntityId { get; set; }

        [Required]
        public string Value { get; set; }

        public string Icon { get; set; }

        public Guid Id { get; set; }


        internal DataPolicyUpdate ToUpdate() => new(
            Id,
            new List<PolicyConditionUpdate>()
            {
                new PolicyConditionUpdate(Operation, new TargetValue(TargetType.Const, Value), Property)
            },
            null,
            Status.ToCore(),
            Comment,
            Icon);
    }


    public abstract class DataAlertViewModelBase : DataAlertViewModel
    {
        public string DisplayComment { get; protected set; }

        public bool IsModify { get; protected set; }


        public DataAlertViewModelBase()
        {
            Actions.Add(new ActionViewModel(true));
        }
    }


    public abstract class DataAlertViewModelBase<T> : DataAlertViewModelBase where T : Core.Model.BaseValue
    {
        public DataAlertViewModelBase(Guid entityId) : base()
        {
            EntityId = entityId;
            IsModify = true;
        }

        public DataAlertViewModelBase(Policy<T> policy, Core.Model.BaseSensorModel sensor) : base()
        {
            EntityId = sensor.Id;
            Id = policy.Id;

            Status = policy.Status.ToClient();
            Comment = policy.Template;
            Icon = policy.Icon;

            if (policy.Conditions?.Count > 0)
            {
                Property = policy.Conditions[0].Property; //should be added as indexed condition block
                Operation = policy.Conditions[0].Operation;
                Value = policy.Conditions[0].Target.Value;
                
                DisplayComment = policy.BuildStateAndComment(sensor.LastValue as T, sensor, policy.Conditions[0]);
            }
        }
    }
}
