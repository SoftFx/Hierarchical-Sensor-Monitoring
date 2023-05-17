using HSMServer.Core.Cache.UpdateEntities;
using System;
using System.Numerics;

namespace HSMServer.Core.Model.Policies
{
    public enum PolicyOperation : byte
    {
        LessThanOrEqual,
        LessThan,
        GreaterThan,
        GreaterThanOrEqual,
        Equal,
        NotEqual,
    }

    public enum TargetType : byte
    {
        Const,
        Sensor,
    }


    public sealed record TargetValue(TargetType Type, string Value);


    public abstract class DataPolicy<T> : Policy where T : BaseValue
    {
        protected override SensorStatus FailStatus => Status;

        protected override string FailMessage => Comment;


        public SensorStatus Status { get; set; }

        public string Comment { get; set; }


        public abstract string Property { get; set; }

        public abstract PolicyOperation Operation { get; set; }

        public abstract TargetValue Target { get; set; }


        internal void Update(DataPolicyUpdate update)
        {
            Operation = update.Operation;
            Property = update.Property;
            Comment = update.Comment;
            Target = update.Target;
            Status = update.Status;
        }

        internal abstract PolicyResult Validate(T value);
    }


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
                if (_operationName == value)
                    return;

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


        internal override PolicyResult Validate(T value)
        {
            return _executeOperation(_getProperty(value), _targetValue) ? PolicyResult.Ok : Fail;
        }
    }


    public abstract class SingleSensorDataPolicy<T, U> : DataPolicy<T, U> where T : BaseValue<U>
    {
        protected override Func<T, U> GetProperty(string property) => DataPolicyBuilder.GetSingleProperty<T, U>(property);
    }

    public abstract class BarSensorDataPolicy<T, U> : DataPolicy<T, U>
        where T : BarBaseValue<U>
        where U : struct, INumber<U>
    {
        protected override Func<T, U> GetProperty(string property) => DataPolicyBuilder.GetBarProperty<T, U>(property);

        protected override Func<U, U, bool> GetOperation(PolicyOperation operation) => DataPolicyBuilder.GetNumberOperation<U>(operation);
    }


    public sealed class IntegerDataPolicy : SingleSensorDataPolicy<IntegerValue, int>
    {
        protected override int GetConstTarget(string strValue) => int.Parse(strValue);

        protected override Func<int, int, bool> GetOperation(PolicyOperation operation) => DataPolicyBuilder.GetNumberOperation<int>(operation);
    }


    public sealed class DoubleBarDataPolicy : BarSensorDataPolicy<DoubleBarValue, double>
    {
        protected override double GetConstTarget(string strValue) => double.Parse(strValue);
    }
}
