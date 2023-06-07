using System;
using System.Collections;
using System.Collections.Generic;

namespace HSMServer.Core.Model.Policies.Infrastructure
{
    public abstract class PolicyCollectionBase<T> : IEnumerable<T>
    {
        internal protected SensorResult Result { get; protected set; } = SensorResult.Ok;

        internal abstract IEnumerable<Guid> Ids { get; }


        internal void Reset() => Result = SensorResult.Ok;


        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public abstract IEnumerator<T> GetEnumerator();
    }
}
