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


        internal OperationViewModel SetData(PolicyOperation? operation, string target)
        {
            if (operation.HasValue)
            {
                Operation = operation.Value;
                Target = target;
            }

            return this;
        }
    }
}
