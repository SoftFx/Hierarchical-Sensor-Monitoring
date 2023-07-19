using HSMCommon.Extensions;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HSMServer.Model.DataAlerts
{
    public class AlertConditionBase
    {
        public string Property { get; set; } //should be changed to enum


        public TimeIntervalViewModel Sensitivity { get; set; }

        public TimeIntervalViewModel TimeToLive { get; set; }


        public PolicyOperation Operation { get; set; }

        public string Target { get; set; }
    }


    public abstract class ConditionViewModel : AlertConditionBase
    {
        public const string StatusCondition = "Status";
        public const string TimeToLiveCondition = "TTL";
        public const string SensitivityCondition = "Sensitivity";


        private readonly List<PolicyOperation> _statusOperations = new()
        {
            PolicyOperation.IsChanged,
            PolicyOperation.IsOk,
            PolicyOperation.IsError
        };


        protected abstract List<string> Properties { get; }

        protected abstract List<PolicyOperation> Operations { get; }


        public List<SelectListItem> PropertiesItems { get; }

        public List<SelectListItem> OperationsItems { get; }

        public List<SelectListItem> StatusOperationsItems { get; }


        public ConditionViewModel(bool isMain)
        {
            Sensitivity = new TimeIntervalViewModel(PredefinedIntervals.ForRestore) { IsAlertBlock = true };
            TimeToLive = new TimeIntervalViewModel(PredefinedIntervals.ForRestore) { IsAlertBlock = true };

            StatusOperationsItems = _statusOperations.ToSelectedItems(k => k.GetDisplayName());
            OperationsItems = Operations.ToSelectedItems(k => k.GetDisplayName());
            PropertiesItems = Properties.ToSelectedItems();

            if (isMain)
                PropertiesItems.Add(new SelectListItem("Inactivity period", TimeToLiveCondition));
            else
                PropertiesItems.Add(new SelectListItem("Sensitivity", SensitivityCondition));

            Property = PropertiesItems.FirstOrDefault()?.Value;
        }
    }


    public sealed class SingleConditionViewModel<T, U> : ConditionViewModel where T : BaseValue<U>, new()
    {
        protected override List<string> Properties { get; } = new()
        {
            nameof(BaseValue<U>.Value),
            nameof(BaseValue<U>.Status)
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
        protected override List<string> Properties { get; } = new()
        {
            nameof(BarBaseValue<U>.Min),
            nameof(BarBaseValue<U>.Max),
            nameof(BarBaseValue<U>.Mean),
            nameof(BarBaseValue<U>.LastValue),
            nameof(BarBaseValue<U>.Status),
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
}
