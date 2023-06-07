using System;
using System.Collections;
using System.Collections.Generic;

namespace HSMServer.Core.Model.Policies.Infrastructure
{
    public abstract class PolicyCollectionBase<T> : IEnumerable<T>
    {
        internal abstract IEnumerable<Guid> Ids { get; }


        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public abstract IEnumerator<T> GetEnumerator();
    }
}
