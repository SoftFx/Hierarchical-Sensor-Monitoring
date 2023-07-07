using System;

namespace HSMServer.Core.Model.Policies
{
    public class PolicyCondition<T, U> : PolicyCondition where T : BaseValue
    {
        private Func<U, U, bool> _executeOperation;
        private Func<T, U> _getProperty;
        private U _targetValue;

        private PolicyOperation _operationName;
        private TargetValue _targetName;
        private string _propertyName;


        internal Func<PolicyOperation, Func<U, U, bool>> OperationBuilder { get; init; }

        internal Func<string, Func<T, U>> PropertyBuilder { get; init; }

        internal Func<string, U> TargetBuilder { get; init; }


        public override PolicyOperation Operation
        {
            get => _operationName;
            set
            {
                _operationName = value;
                _executeOperation = OperationBuilder(value);
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
                _getProperty = PropertyBuilder(value);
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

                _targetValue = Target.Type switch
                {
                    TargetType.Const => TargetBuilder(value.Value),
                    _ => default,
                };
            }
        }


        internal bool Check(T value) => _executeOperation(_getProperty(value), _targetValue);
    }
}