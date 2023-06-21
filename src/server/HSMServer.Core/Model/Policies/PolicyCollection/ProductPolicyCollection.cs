using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.Policies
{
    public class ProductPolicyCollection : PolicyCollectionBase
    {
        private readonly List<Policy> _basePolicies = new(1 << 4);


        internal override IEnumerable<Guid> Ids => _basePolicies.Select(u => u.Id);

        public override IEnumerator<Policy> GetEnumerator() => _basePolicies.GetEnumerator();


        internal override void AddPolicy<T>(T policy)
        {
            throw new NotImplementedException();
        }
    }
}