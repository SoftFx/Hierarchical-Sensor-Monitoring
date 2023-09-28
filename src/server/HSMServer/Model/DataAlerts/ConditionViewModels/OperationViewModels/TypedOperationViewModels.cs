using HSMServer.Core.Model.Policies;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
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
}
