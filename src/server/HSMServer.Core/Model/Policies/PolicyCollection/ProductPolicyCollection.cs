using System;
using System.Collections.Generic;

namespace HSMServer.Core.Model.Policies
{
    public class ProductPolicyCollection : PolicyCollectionBase
    {
        internal override IEnumerable<Guid> Ids => throw new NotImplementedException();

        public override IEnumerator<Policy> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        internal override void AddPolicy<T>(T policy)
        {
            throw new NotImplementedException();
        }
    }
}