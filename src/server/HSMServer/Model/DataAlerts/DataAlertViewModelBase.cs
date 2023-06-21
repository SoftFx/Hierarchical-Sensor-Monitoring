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


        internal DataPolicyUpdate ToUpdate() =>
            new(Id, Property, Operation, new TargetValue(TargetType.Const, Value), Status.ToCore(), Comment, Icon);
    }


    public abstract class DataAlertViewModelBase : DataAlertViewModel
    {
        private readonly List<string> _icons = new() { "⬆️", "⏫", "🔼", "↕️", "🔽", "⏬", "⬇️" };
        private readonly Dictionary<SensorStatus, string> _statuses = new()
        {
            { SensorStatus.Ok, "nothing" },
            { SensorStatus.Warning, $"{SensorStatus.Warning.ToSelectIcon()} {SensorStatus.Warning.GetDisplayName()}" },
            { SensorStatus.Error, $"{SensorStatus.Error.ToSelectIcon()} {SensorStatus.Error.GetDisplayName()}" },
        };


        public abstract string DisplayComment { get; }

        protected abstract List<string> Properties { get; }

        protected abstract List<PolicyOperation> Actions { get; }


        public bool IsModify { get; protected set; }


        public List<SelectListItem> PropertiesItems { get; }

        public List<SelectListItem> ActionsItems { get; }

        public List<SelectListItem> StatusesItems { get; }

        public List<SelectListItem> IconsItems { get; }


        public DataAlertViewModelBase()
        {
            PropertiesItems = Properties.Select(p => new SelectListItem(p, p)).ToList();
            ActionsItems = Actions.Select(a => new SelectListItem(a.GetDisplayName(), $"{a}")).ToList();

            IconsItems = _icons.Select(i => new SelectListItem(i.ToIconUnicode(), i)).ToList();
            StatusesItems = _statuses.Select(s => new SelectListItem(s.Value, $"{s.Key}")).ToList();
        }
    }


    public abstract class DataAlertViewModelBase<T> : DataAlertViewModelBase where T : Core.Model.BaseValue
    {
        public DataAlertViewModelBase(Guid entityId) : base()
        {
            EntityId = entityId;
            IsModify = true;
        }

        public DataAlertViewModelBase(DataPolicy<T> policy, Core.Model.BaseSensorModel sensor) : base()
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
