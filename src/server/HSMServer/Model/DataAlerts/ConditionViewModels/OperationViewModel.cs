using HSMCommon.Extensions;
using HSMServer.Core.Model.Policies;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class OperationViewModel
    {
        public List<SelectListItem> OperationsItems { get; }

        public PolicyOperation Operation { get; }

        public string Target { get; }


        public OperationViewModel() { }

        internal OperationViewModel(string property)
        {

        }

        internal OperationViewModel(ConditionViewModel condition)
        {
            Target = condition.Target;
            Operation = condition.Operation;
            OperationsItems = condition.GetOperations().ToSelectedItems(k => k.GetDisplayName());
        }
    }
}
