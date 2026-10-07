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

            // Target-less operations never read the constant: their executors
            // compare against the sensor's own previous value or ignore the
            // target, which is exactly what the editor submits for them. A
            // Const target from an API client or a stored entity is therefore
            // evaluated as LastValue(self) — building the typed const builder
            // instead would hand those executors a Func<U> they cannot run,
            // and parsing the value could throw before the operation is even
            // consulted (#1439).
            object targetBuilder = value.Type switch
            {
                TargetType.Const when !IsTargetless(Operation) => BuildConstTargetBuilder(value.Value),
                TargetType.Const or TargetType.LastValue => _getLastValue,
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