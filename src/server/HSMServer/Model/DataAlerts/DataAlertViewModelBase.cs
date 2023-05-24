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
        public Guid Id { get; set; }

        public string Property { get; set; }

        public PolicyOperation Operation { get; set; }

        public string Value { get; set; }

        public SensorStatus Status { get; set; }

        public string Comment { get; set; }


        public DataAlertViewModel() { }


        internal DataPolicyUpdate ToUpdate() =>
            new(Id, Property, Operation, new TargetValue(TargetType.Const, Value), Status.ToCore(), Comment);
    }


    public abstract class DataAlertViewModelBase : DataAlertViewModel
    {
        private readonly List<SensorStatus> _statuses = new() { SensorStatus.Error, SensorStatus.Warning };


        protected abstract List<string> Properties { get; }

        protected abstract List<PolicyOperation> Actions { get; }


        public string DisplayComment { get; protected set; }

        public required bool IsModify { get; init; }


        public List<SelectListItem> PropertiesItems => Properties.Select(p => new SelectListItem(p, p)).ToList();

        public List<SelectListItem> ActionsItems => Actions.Select(a => new SelectListItem(a.GetDisplayName(), $"{a}")).ToList();

        public List<SelectListItem> StatusesItems => _statuses.Select(s => new SelectListItem(s.GetDisplayName(), $"{s}")).ToList();


        public DataAlertViewModelBase() : base() { }
    }


    public abstract class DataAlertViewModelBase<T> : DataAlertViewModelBase where T : Core.Model.BaseValue
    {
        public DataAlertViewModelBase() : base() { }

        public DataAlertViewModelBase(DataPolicy<T> policy)
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
