using HSMCommon.Extensions;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.DataAlerts
{
    public class DataAlertViewModel
    {
        public PolicyOperation Operation { get; set; }

        public SensorStatus Status { get; set; }

        public string Property { get; set; }

        public string Comment { get; set; }

        public string Value { get; set; }

        public Guid Id { get; set; }


        internal DataPolicyUpdate ToUpdate() =>
            new(Id, Property, Operation, new TargetValue(TargetType.Const, Value), Status.ToCore(), Comment);
    }


    public abstract class DataAlertViewModelBase : DataAlertViewModel
    {
        private readonly List<SensorStatus> _statuses = new() { SensorStatus.Error, SensorStatus.Warning };


        public abstract string DisplayComment { get; }

        protected abstract List<string> Properties { get; }

        protected abstract List<PolicyOperation> Actions { get; }


        public bool IsModify { get; protected set; }


        public List<SelectListItem> PropertiesItems { get; }

        public List<SelectListItem> ActionsItems { get; }

        public List<SelectListItem> StatusesItems { get; }


        public DataAlertViewModelBase()
        {
            PropertiesItems = Properties.Select(p => new SelectListItem(p, p)).ToList();
            ActionsItems = Actions.Select(a => new SelectListItem(a.GetDisplayName(), $"{a}")).ToList();
            StatusesItems = _statuses.Select(s => new SelectListItem($"{s.ToSelectIcon()} {s.GetDisplayName()}", $"{s}")).ToList();
        }
    }


    public abstract class DataAlertViewModelBase<T> : DataAlertViewModelBase where T : Core.Model.BaseValue
    {
        public DataAlertViewModelBase() : base()
        {
            IsModify = true;
        }

        public DataAlertViewModelBase(DataPolicy<T> policy) : base()
        {
            Id = policy.Id;
            Property = policy.Property;
            Operation = policy.Operation;
            Value = policy.Target.Value;
            Status = policy.Status.ToClient();
            Comment = policy.Comment;
        }
    }
}
