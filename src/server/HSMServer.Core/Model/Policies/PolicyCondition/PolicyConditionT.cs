using System;
using HSMCommon.Model;


namespace HSMServer.Core.Model.Policies
{
    internal interface IPolicyCondition<T> where T : BaseValue
    {
        internal bool Check(T value);
    }


    public abstract class PolicyCondition<T, U> : PolicyCondition, IPolicyCondition<T> where T : BaseValue
    {
        private PolicyOperation _operationName;
        private PolicyProperty _propertyName;
        private TargetValue _targetName;

        private PolicyExecutor _executor;
        private U _targetConstConverter;


        internal abstract U TargetConstConverter(string str);


        public override PolicyOperation Operation
        {
            get => _operationName;
            set
            {
                _operationName = value;
                _executor?.SetOperation(value);
            }
        }

        public override PolicyProperty Property
        {
            get => _propertyName;
            set
            {
                _propertyName = value;

                _executor = PolicyExecutorBuilder.BuildExecutor<T, U>(value);
                _executor.SetOperation(Operation);

                SetTarget(Target);
            }
        }

        public override TargetValue Target
        {
            get => _targetName;
            set
            {
                _targetName = value;

                SetTarget(value);
            }
        }


        bool IPolicyCondition<T>.Check(T value) => _executor.Execute(value);


        private void SetTarget(TargetValue value)
        {
            if (value is null)
                return;

            object targetBuilder = value.Type switch
            {
                TargetType.Const => BuildConstTargetBuilder(value.Value),
                TargetType.LastValue => _getLastValue,
                _ => throw new NotImplementedException($"Unsupported target type {value.Type}"),
            };

            _executor?.SetTarget(targetBuilder);
        }

        private Func<U> BuildConstTargetBuilder(string val)
        {
            U GetConstTarget() => _targetConstConverter;

            _targetConstConverter = TargetConstConverter(val);

            return GetConstTarget;
        }
    }
}