using HSMCommon.Extensions;
using HSMServer.Core.Model.Policies;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.DataAlerts
{
    public abstract class OperationViewModel
    {
        protected abstract List<PolicyOperation> Operations { get; }

        public abstract bool IsTargetRequired { get; }


        public List<SelectListItem> OperationsItems { get; }


        public PolicyOperation Operation { get; private set; }

        public string Target { get; private set; }


        internal OperationViewModel()
        {
            OperationsItems = Operations.ToSelectedItems(i => i.GetDisplayName());
            Operation = Operations.FirstOrDefault();
        }


        internal OperationViewModel SetData(PolicyOperation operation, string target)
        {
            Operation = operation;
            Target = target;

            return this;
        }
    }


    public sealed class StatusOperation : OperationViewModel
    {
        protected override List<PolicyOperation> Operations { get; } = new()
        {
            PolicyOperation.IsChanged,
            PolicyOperation.IsChangedToOk,
            PolicyOperation.IsChangedToError,
            PolicyOperation.IsOk,
            PolicyOperation.IsError
        };

        public override bool IsTargetRequired { get; } = false;
    }


    public sealed class CommentOperation : OperationViewModel
    {
        protected override List<PolicyOperation> Operations { get; } = new()
        {
            PolicyOperation.Equal,
            PolicyOperation.NotEqual,
            PolicyOperation.Contains,
            PolicyOperation.StartsWith,
            PolicyOperation.EndsWith,
            PolicyOperation.IsChanged,
        };

        public override bool IsTargetRequired { get; } = false;
    }


    public sealed class NumericOperation : OperationViewModel
    {
        protected override List<PolicyOperation> Operations { get; } = new()
        {
            PolicyOperation.LessThanOrEqual,
            PolicyOperation.LessThan,
            PolicyOperation.GreaterThan,
            PolicyOperation.GreaterThanOrEqual,
            PolicyOperation.NotEqual,
            PolicyOperation.Equal,
        };

        public override bool IsTargetRequired { get; } = true;
    }


    public sealed class StringOperation : OperationViewModel
    {
        protected override List<PolicyOperation> Operations { get; } = new()
        {
            PolicyOperation.Equal,
            PolicyOperation.NotEqual,
            PolicyOperation.Contains,
            PolicyOperation.StartsWith,
            PolicyOperation.EndsWith,
        };

        public override bool IsTargetRequired { get; } = false;
    }


    public abstract class IntervalOperationViewModel
    {
        public TimeIntervalViewModel Target { get; } = new();

        public abstract string TargetName { get; }

        public abstract string Operation { get; }


        protected IntervalOperationViewModel(TimeIntervalViewModel interval)
        {
            Target = interval;
        }
    }


    public sealed class TimeToLiveOperation : IntervalOperationViewModel
    {
        public override string TargetName { get; } = nameof(AlertConditionBase.TimeToLive);

        public override string Operation { get; } = "is";


        internal TimeToLiveOperation(TimeIntervalViewModel interval) : base(interval) { }
    }


    public sealed class SensitivityOperation : IntervalOperationViewModel
    {
        public override string TargetName { get; } = nameof(AlertConditionBase.Sensitivity);

        public override string Operation { get; } = "is more than";


        internal SensitivityOperation(TimeIntervalViewModel interval) : base(interval) { }
    }
}
