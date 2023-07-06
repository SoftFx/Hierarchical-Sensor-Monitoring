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

        //public TimeIntervalViewModel Sensitivity { get; set; }


        internal DataPolicyUpdate ToUpdate() =>
            new(Id, Property, Operation, new TargetValue(TargetType.Const, Value), Status.ToCore(), Comment, Icon);
    }


    public abstract class DataAlertViewModelBase : DataAlertViewModel
    {
        private const string DefaultCommentTemplate = "$sensor $action $target";

        public const string TimeToLiveCondition = "TTL";
        public const string SensitivityCondition = "Sensitivity";

        public const string ShowIconAction = "Show icon";
        public const string SetStatusAction = "Set status";
        public const string SendNotifyAction = "Send notification";


        public abstract string DisplayComment { get; }

        protected abstract List<string> Icons { get; }

        protected abstract List<string> Properties { get; }

        protected abstract List<PolicyOperation> Operations { get; }


        public bool IsModify { get; protected set; }


        public List<SelectListItem> PropertiesItems { get; }

        public List<SelectListItem> ActionsItems { get; }

        public List<SelectListItem> StatusesItems { get; }

        public List<SelectListItem> IconsItems { get; }

        public TimeIntervalViewModel Sensitivity { get; } = new TimeIntervalViewModel(PredefinedIntervals.ForRestore);

        public TimeIntervalViewModel TimeToLive { get; } = new TimeIntervalViewModel(PredefinedIntervals.ForTimeout);


        public List<ConditionViewModel> Conditions { get; } = new();
        public List<ActionViewModel> Actions { get; } = new() { new ActionViewModel(true) };


        public DataAlertViewModelBase()
        {
            Comment = DefaultCommentTemplate;

            PropertiesItems = Properties.Select(p => new SelectListItem(p, p, false)).ToList();
            IconsItems = Icons.Select(i => new SelectListItem(i.ToIconUnicode(), i)).ToList();
            ActionsItems = Operations.Select(a => new SelectListItem(a.GetDisplayName(), $"{a}")).ToList();
            StatusesItems = AlertPredefined.Statuses.Select(s => new SelectListItem(s.Value, $"{s.Key}")).ToList();

            //Status = SensorStatus.Ok;
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
            Property = policy.Property;
            Operation = policy.Operation;
            Value = policy.Target.Value;
            Status = policy.Status.ToClient();
            Comment = policy.Template;
            Icon = policy.Icon;
        }
    }
}
