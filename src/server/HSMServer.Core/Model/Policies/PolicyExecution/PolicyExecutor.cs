using System;
using HSMCommon.Model;


namespace HSMServer.Core.Model.Policies
{
    internal abstract class PolicyExecutor
    {
        internal abstract bool Execute(BaseValue value);

        internal abstract void SetOperation(PolicyOperation operation);

        internal abstract void SetTarget(object target);
    }


    internal abstract class PolicyExecutor<T> : PolicyExecutor
    {
        private protected Func<T, T, bool> _executeOperation;
        private protected Func<T> _targetBuilder;


        internal override void SetOperation(PolicyOperation operation) => _executeOperation = GetTypedOperation(operation);

        internal override void SetTarget(object target) => _targetBuilder = target switch
        {
            Func<T> constBuilder => constBuilder,
            Func<BaseValue> getLastValue => () => GetCheckedValue(getLastValue()),
            _ => throw new NotImplementedException($"Notsupported alert target type"),
        };


        internal override bool Execute(BaseValue value) => _executeOperation(GetCheckedValue(value), _targetBuilder());


        protected abstract Func<T, T, bool> GetTypedOperation(PolicyOperation operation);

        protected abstract T GetCheckedValue(BaseValue value);
    }
}