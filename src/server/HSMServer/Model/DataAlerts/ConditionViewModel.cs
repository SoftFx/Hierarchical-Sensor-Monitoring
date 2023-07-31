﻿using HSMCommon.Extensions;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Numerics;

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
            PolicyOperation.IsOk,
            PolicyOperation.IsError
        };


        protected abstract List<AlertProperty> Properties { get; }

        protected abstract List<PolicyOperation> Operations { get; }


        public bool IsMain { get; }

        public List<SelectListItem> PropertiesItems { get; }

        public List<SelectListItem> OperationsItems { get; }

        public List<SelectListItem> StatusOperationsItems { get; }


        public ConditionViewModel(bool isMain)
        {
            IsMain = isMain;

            Sensitivity = new TimeIntervalViewModel(PredefinedIntervals.ForRestore) { IsAlertBlock = true };
            TimeToLive = new TimeIntervalViewModel(PredefinedIntervals.ForTimeout) { IsAlertBlock = true };

            StatusOperationsItems = _statusOperations.ToSelectedItems(k => k.GetDisplayName());
            OperationsItems = Operations?.ToSelectedItems(k => k.GetDisplayName());
            PropertiesItems = Properties.ToSelectedItems(k => k.GetDisplayName());

            //if (!isMain)
            //    PropertiesItems.Add(new SelectListItem(AlertProperty.Sensitivity.GetDisplayName(), nameof(AlertProperty.Sensitivity)));

            Property = Enum.Parse<AlertProperty>(PropertiesItems.FirstOrDefault()?.Value);
        }
    }


    public sealed class ConditionViewModel<T> : ConditionViewModel where T : BaseValue
    {
        protected override List<AlertProperty> Properties { get; } = new() { AlertProperty.Status };

        protected override List<PolicyOperation> Operations { get; }


        public ConditionViewModel(bool isMain) : base(isMain) { }
    }


    public sealed class SingleConditionViewModel<T, U> : ConditionViewModel where T : BaseValue<U>, new()
    {
        protected override List<AlertProperty> Properties { get; } = new()
        {
            AlertProperty.Value,
            AlertProperty.Status
        };

        protected override List<PolicyOperation> Operations { get; } = new()
        {
            PolicyOperation.LessThanOrEqual,
            PolicyOperation.LessThan,
            PolicyOperation.GreaterThan,
            PolicyOperation.GreaterThanOrEqual,
        };


        public SingleConditionViewModel(bool isMain) : base(isMain) { }
    }


    public sealed class BarConditionViewModel<T, U> : ConditionViewModel where T : BarBaseValue<U>, new() where U : INumber<U>
    {
        protected override List<AlertProperty> Properties { get; } = new()
        {
            AlertProperty.Min,
            AlertProperty.Max,
            AlertProperty.Mean,
            AlertProperty.LastValue,
            AlertProperty.Status,
        };

        protected override List<PolicyOperation> Operations { get; } = new()
        {
            PolicyOperation.LessThanOrEqual,
            PolicyOperation.LessThan,
            PolicyOperation.GreaterThan,
            PolicyOperation.GreaterThanOrEqual,
        };


        public BarConditionViewModel(bool isMain) : base(isMain) { }
    }


    public sealed class TimeToLiveConditionViewModel : ConditionViewModel
    {
        protected override List<AlertProperty> Properties { get; } = new() { AlertProperty.TimeToLive };

        protected override List<PolicyOperation> Operations { get; }


        public TimeToLiveConditionViewModel(bool isMain = true) : base(isMain) { }
    }
}
