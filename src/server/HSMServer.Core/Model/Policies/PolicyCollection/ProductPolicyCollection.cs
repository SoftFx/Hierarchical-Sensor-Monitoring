using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.Policies
{
    public sealed class ProductPolicyCollection : PolicyCollectionBase
    {
        private readonly ConcurrentDictionary<string, PolicyGroup> _groups = new();

        public List<PolicyGroup> GroupedPolicies => _groups.Values.ToList();


        internal ProductPolicyCollection()
        {

        }
    }
}