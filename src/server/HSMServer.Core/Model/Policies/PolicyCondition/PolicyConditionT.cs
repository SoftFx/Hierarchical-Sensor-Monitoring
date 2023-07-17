using System;

namespace HSMServer.Core.Model.Policies
{
    public class PolicyCondition<T, U> : PolicyCondition where T : BaseValue
    {
        private PolicyOperation _operationName;
        private TargetValue _targetName;
        private string _propertyName;

        private PolicyExecutor _executor;
        private U _constTargetValue;


        internal Func<string, U> ConstTargetValueConverter { get; init; }

        internal Func<BaseValue> GetLastTargetValue { get; init; }


        public override PolicyOperation Operation
        {
            get => _operationName;
            set
            {
                _operationName = value;
                _executor?.SetOperation(value);
            }
        }

        public override string Property
        {
            get => _propertyName;
            set
            {
                if (_propertyName == value)
                    return;

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
                if (_targetName == value)
                    return;

                _targetName = value;

                SetTarget(value);
            }
        }


        internal bool Check(T value) => _executor.Execute(value);


        private void SetTarget(TargetValue value)
        {
            Func<U> BuildConstTargetBuilder(string val)
            {
                U GetConstTarget() => _constTargetValue;

                _constTargetValue = ConstTargetValueConverter(val);

                return GetConstTarget;
            }

            object targetBuilder = value.Type switch
            {
                TargetType.Const => BuildConstTargetBuilder(value.Value),
                TargetType.LastValue => GetLastTargetValue,
                _ => throw new NotImplementedException($"Unsupported target type {value.Type}"),
            };

            _executor?.SetTarget(targetBuilder);
        }
    }
}