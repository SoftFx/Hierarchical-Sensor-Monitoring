using HSMServer.Core.Model.Policies;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

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

        public override bool IsTargetRequired => false;
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

        public override bool IsTargetRequired => true;
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

        public override bool IsTargetRequired => true;
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

        public override bool IsTargetRequired => true;
    }

    public sealed class VersionOperation : OperationViewModel
    {
        protected override List<PolicyOperation> Operations { get; } = new()
        {
            PolicyOperation.Equal,
            PolicyOperation.NotEqual,
            PolicyOperation.Contains,
            PolicyOperation.StartsWith,
            PolicyOperation.EndsWith,
        };

        public override bool IsTargetRequired => true;

        //public override string Pattern => "\\d+\\.\\d+\\.\\d+";
    }

}
