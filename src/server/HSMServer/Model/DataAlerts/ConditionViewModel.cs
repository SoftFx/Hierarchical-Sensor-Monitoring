using HSMCommon.Extensions;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.DataAlerts
{
    public abstract class ConditionViewModel
    {
        public const string TimeToLiveCondition = "TTL";
        public const string SensitivityCondition = "Sensitivity";


        protected abstract List<string> Properties { get; }

        protected abstract List<PolicyOperation> Actions { get; }


        public List<SelectListItem> PropertiesItems { get; }

        public List<SelectListItem> ActionsItems { get; }


        public TimeIntervalViewModel Sensitivity { get; } = new TimeIntervalViewModel(PredefinedIntervals.ForRestore);

        public TimeIntervalViewModel TimeToLive { get; } = new TimeIntervalViewModel(PredefinedIntervals.ForTimeout);


        public ConditionViewModel(bool isFirst)
        {
            ActionsItems = Actions.Select(a => new SelectListItem(a.GetDisplayName(), $"{a}")).ToList();
            PropertiesItems = Properties.Select(p => new SelectListItem(p, p, false)).ToList();

            if (isFirst)
                PropertiesItems.Add(new SelectListItem("Inactivity period", TimeToLiveCondition));
            else
                PropertiesItems.Add(new SelectListItem("Sensitivity", SensitivityCondition));
        }
    }


    public sealed class SingleConditionViewModel<T, U> : ConditionViewModel where T : BaseValue<U>, new()
    {
        protected override List<string> Properties { get; } = new() { nameof(BaseValue<U>.Value) };

        protected override List<PolicyOperation> Actions { get; } = new()
        {
            PolicyOperation.LessThanOrEqual,
            PolicyOperation.LessThan,
            PolicyOperation.GreaterThan,
            PolicyOperation.GreaterThanOrEqual,
        };


        public SingleConditionViewModel(bool isFirst) : base(isFirst) { }
    }


    public sealed class BarConditionViewModel<T, U> : ConditionViewModel where T : BarBaseValue<U>, new() where U : struct
    {
        protected override List<string> Properties { get; } = new()
        {
            nameof(BarBaseValue<U>.Min),
            nameof(BarBaseValue<U>.Max),
            nameof(BarBaseValue<U>.Mean),
            nameof(BarBaseValue<U>.LastValue),
        };

        protected override List<PolicyOperation> Actions { get; } = new()
        {
            PolicyOperation.LessThanOrEqual,
            PolicyOperation.LessThan,
            PolicyOperation.GreaterThan,
            PolicyOperation.GreaterThanOrEqual,
        };


        public BarConditionViewModel(bool isFirst) : base(isFirst) { }
    }
}
