using System;

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
        private U _constTargetValue;


        internal abstract U ConstTargetValueConverter(string str);


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

                _executor = PolicyExecutorBuilder.BuildExecutor<U>(value);
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
            Func<U> BuildConstTargetBuilder(string val)
            {
                U GetConstTarget() => _constTargetValue;

                _constTargetValue = ConstTargetValueConverter(val);

                return GetConstTarget;
            }

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
    }
}