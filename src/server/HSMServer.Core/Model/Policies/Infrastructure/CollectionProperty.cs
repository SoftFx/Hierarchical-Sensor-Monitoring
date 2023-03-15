using HSMServer.Core.Cache;
using System;

namespace HSMServer.Core.Model.Policies
{
    public abstract class CollectionProperty
    {
        internal CollectionProperty ParentProperty { get; set; }

        public abstract Guid PolicyGuid { get; }

        public abstract bool IsEmpty { get; }

        public abstract bool IsSet { get; }


        public Action<ActionType, Policy> Uploaded;


        internal abstract void SetPolicy(Policy policy);
    }


    public sealed class CollectionProperty<T> : CollectionProperty where T : ServerPolicy, new()
    {
        private readonly T _emptyPolicy = new();
        private T _curPolicy;


        public override Guid PolicyGuid => _curPolicy?.Id ?? Guid.Empty;

        public override bool IsEmpty => Policy == null;

        public override bool IsSet => _curPolicy != null;


        public T Policy => _curPolicy ?? ((CollectionProperty<T>)ParentProperty)?.Policy ?? _emptyPolicy;


        internal override void SetPolicy(Policy policy)
        {
            var newPolicy = (T)policy;
            var action = ActionType.Add;

            if (IsSet)
            {
                policy = _curPolicy;

                if (newPolicy.FromParent)
                {
                    _curPolicy = null;
                    action = ActionType.Delete;
                }
                else
                {
                    _curPolicy.Interval = newPolicy.Interval;
                    action = ActionType.Update;
                }
            }
            else if (!newPolicy.FromParent)
                _curPolicy = newPolicy;
            else
                return;

            Uploaded?.Invoke(action, policy);
        }
    }
}
