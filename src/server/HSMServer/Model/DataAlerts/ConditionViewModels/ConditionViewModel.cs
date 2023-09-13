using HSMCommon.Extensions;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace HSMServer.Model.DataAlerts
{
    public enum AlertProperty
    {
        Status,
        Comment,
        Value,
        Min,
        Max,
        Mean,
        Count,
        LastValue,
        Length,
        [Display(Name = "Size")]
        OriginalSize,
        [Display(Name = "New data")]
        NewSensorData,
        Sensitivity,
        [Display(Name = "Inactivity period")]
        TimeToLive,
    }


    public class AlertConditionBase
    {
        public AlertProperty Property { get; set; }


        public TimeIntervalViewModel Sensitivity { get; set; }

        public TimeIntervalViewModel TimeToLive { get; set; }


        public PolicyOperation Operation { get; set; }

        public string Target { get; set; }
    }


    public abstract class ConditionViewModel : AlertConditionBase
    {
        private readonly List<PolicyOperation> _statusOperations = new()
        {
            PolicyOperation.IsChanged,
            PolicyOperation.IsChangedToOk,
            PolicyOperation.IsChangedToError,
            PolicyOperation.IsOk,
            PolicyOperation.IsError
        };

        protected readonly List<PolicyOperation> _stringOperations = new()
        {
            PolicyOperation.Equal,
            PolicyOperation.NotEqual,
            PolicyOperation.Contains,
            PolicyOperation.StartsWith,
            PolicyOperation.EndsWith,
            PolicyOperation.IsChanged,
        };


        protected abstract List<AlertProperty> Properties { get; }

        protected abstract List<PolicyOperation> Operations { get; }


        public bool IsMain { get; }

        public List<SelectListItem> PropertiesItems { get; }

        public List<SelectListItem> OperationsItems { get; }

        public List<SelectListItem> StatusOperationsItems { get; }

        public List<SelectListItem> StringOperationsItems { get; }

        public OperationViewModel OperationViewModel { get; set; }


        public ConditionViewModel(bool isMain)
        {
            IsMain = isMain;

            Sensitivity = new TimeIntervalViewModel(PredefinedIntervals.ForRestore) { IsAlertBlock = true };
            TimeToLive = new TimeIntervalViewModel(PredefinedIntervals.ForTimeout) { IsAlertBlock = true };

            StatusOperationsItems = _statusOperations.ToSelectedItems(k => k.GetDisplayName());
            StringOperationsItems = _stringOperations.ToSelectedItems(k => k.GetDisplayName());
            OperationsItems = Operations?.ToSelectedItems(k => k.GetDisplayName());
            PropertiesItems = Properties.ToSelectedItems(k => k.GetDisplayName());

            //if (!isMain)
            //    PropertiesItems.Add(new SelectListItem(AlertProperty.Sensitivity.GetDisplayName(), nameof(AlertProperty.Sensitivity)));

            Property = Enum.Parse<AlertProperty>(PropertiesItems.FirstOrDefault()?.Value);
        }
    }


    public sealed class ConditionViewModel<T> : ConditionViewModel where T : BaseValue
    {
        protected override List<AlertProperty> Properties { get; } = new()
        {
            AlertProperty.Status,
            AlertProperty.Comment,
            AlertProperty.NewSensorData,
        };

        protected override List<PolicyOperation> Operations { get; }


        public ConditionViewModel(bool isMain) : base(isMain) { }
    }
}
