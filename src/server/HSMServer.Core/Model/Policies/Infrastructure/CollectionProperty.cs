using System;

namespace HSMServer.Core.Model.Policies
{
    public abstract class CollectionProperty
    {
        internal CollectionProperty ParentProperty { get; set; }

        public abstract Guid PolicyGuid { get; }

        public abstract bool IsEmpty { get; }


        internal abstract void SetPolicy(ServerPolicy policy);
    }


    public sealed class CollectionProperty<T> : CollectionProperty where T : ServerPolicy
    {
        private T _policy;


        public override Guid PolicyGuid => _policy?.Id ?? Guid.Empty;

        public override bool IsEmpty => _policy == null;


        public T Policy => _policy ?? ((CollectionProperty<T>)ParentProperty)?.Policy;


        public Action Updated; // TODO remove?


        internal override void SetPolicy(ServerPolicy policy)
        {
            _policy = (T)policy;

            Updated?.Invoke();
        }
    }
}
