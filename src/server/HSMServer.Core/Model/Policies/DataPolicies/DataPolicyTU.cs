using System;

namespace HSMServer.Core.Model.Policies
{
    public abstract class DataPolicy<T, U> : DataPolicy<T> where T : BaseValue
    {
        private Func<U, U, bool> _executeOperation;
        private Func<T, U> _getProperty;
        private U _targetValue;

        private PolicyOperation _operationName;
        private TargetValue _targetName;
        private string _propertyName;


        public override PolicyOperation Operation
        {
            get => _operationName;
            set
            {
                _operationName = value;
                _executeOperation = GetOperation(value);
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
                _getProperty = GetProperty(value);
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
                    TargetType.Const => GetConstTarget(value.Value),
                    _ => default,
                };
            }
        }


        protected abstract Func<U, U, bool> GetOperation(PolicyOperation operation);

        protected abstract Func<T, U> GetProperty(string property);

        protected abstract U GetConstTarget(string strValue);


        //internal override SensorResult Validate(T value, BaseSensorModel sensor)
        //{
        //    return _executeOperation(_getProperty(value), _targetValue)
        //        ? new(Status, GetComment(value, sensor), "↕️")
        //        : SensorResult.Ok;
        //}
    }
}