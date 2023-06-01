using System;
using System.Collections;
using System.Collections.Generic;

namespace HSMServer.Core.Model.Policies.Infrastructure
{
    public abstract class PolicyCollectionBase<T> : IEnumerable<T>
    {
        internal protected PolicyResult Result { get; protected set; } = PolicyResult.Ok;

        internal abstract IEnumerable<Guid> Ids { get; }


        internal void Reset() => Result = PolicyResult.Ok;


        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public abstract IEnumerator<T> GetEnumerator();
    }
}
