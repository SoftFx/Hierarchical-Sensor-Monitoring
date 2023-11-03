using HSMServer.Core.Model.Policies;
using System.Collections.Generic;

namespace HSMServer.Core.Extensions
{
    internal static class PolicyExtensions
    {
        public static bool IsStatusChangeResult(this List<PolicyCondition> conditions)
        {
            if (conditions.Count != 1)
                return false;

            var condition = conditions[0];

            return condition.Property is PolicyProperty.Status && condition.Operation is PolicyOperation.IsChanged;
        }
    }
}