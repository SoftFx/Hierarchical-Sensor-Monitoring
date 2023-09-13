using HSMCommon.Extensions;
using HSMServer.Core.Model.Policies;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class OperationListItem : SelectListItem
    {
        public bool ShowTarget { get; set; }


        public OperationListItem(KeyValuePair<PolicyOperation, bool> pair)
        {
            Value = pair.Key.ToString();
            Text = pair.Key.GetDisplayName();

            ShowTarget = pair.Value;
        }
    }


    public abstract class OperationViewModel
    {
        protected abstract List<PolicyOperation> Operations { get; }


        public List<SelectListItem> OperationsItems { get; }


        public PolicyOperation SelectedOperation { get; private set; }

        public string Target { get; private set; }


        internal OperationViewModel()
        {
            OperationsItems = Operations.ToSelectedItems(i => i.GetDisplayName());
        }


        internal OperationViewModel SetData(PolicyOperation? operation, string target)
        {
            SelectedOperation = operation ?? SelectedOperation;
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
}
