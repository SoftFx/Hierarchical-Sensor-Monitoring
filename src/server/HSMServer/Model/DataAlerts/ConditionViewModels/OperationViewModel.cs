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
    }
}
