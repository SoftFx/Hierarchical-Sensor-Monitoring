using HSMServer.Core.Cache.UpdateEntities;
using System;
using System.Numerics;

namespace HSMServer.Core.Model.Policies
{
    public enum Operation : byte
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


    public abstract class DataPolicy : Policy
    {
        protected override SensorStatus FailStatus => Status;

        protected override string FailMessage => Comment;


        public string Property { get; set; }

        public Operation Action { get; set; }

        public TargetValue Target { get; set; }

        public SensorStatus Status { get; set; }

        public string Comment { get; set; }


        public DataPolicy() : base() { }


        internal abstract DataPolicy Initialize();

        internal DataPolicy Update(DataPolicyUpdate update)
        {
            Property = update.Property;
            Action = update.Action;
            Target = update.Target;
            Status = update.Status;
            Comment = update.Comment;

            return Initialize();
        }
    }


    public abstract class DataPolicy<T, U> : DataPolicy where T : BaseValue
    {
        protected private Func<U, U, bool> _operation;
        protected private Func<T, U> _getProperty;
        protected private U _targetValue;

        public DataPolicy() : base() { }


        internal PolicyResult Validate(T value)
        {
            return _operation(_getProperty(value), _targetValue) ? PolicyResult.Ok : Fail;
        }

        internal override DataPolicy Initialize()
        {
            _targetValue = Target.Type switch
            {
                TargetType.Const => GetConstTarget(),
                _ => default,
            };

            _operation = GetOperation();
            _getProperty = GetProperty(); // typeof(T) == typeof(BarBaseValue) ? PolicyBuilder.GetBarProperty<T, U>(Property) : PolicyBuilder.GetSimpleProperty<T, U>(Property);

            return this;
        }


        protected abstract Func<U, U, bool> GetOperation();

        protected abstract Func<T, U> GetProperty();

        protected abstract U GetConstTarget();
    }


    public abstract class SimpleDataPolicy<T, U> : DataPolicy<T, U> where T : BaseValue<U> where U : INumber<U>
    {
        protected override Func<T, U> GetProperty() => Property switch
        {
            nameof(BaseValue<U>.Value) => (T value) => value.Value,
            _ => default,
        };
        protected override Func<U, U, bool> GetOperation() => PolicyBuilder.BuilNumberOperation<U>(Action);
    }


    public sealed class IntegerDataPolicy : SimpleDataPolicy<IntegerValue, int>
    {
        protected override int GetConstTarget() => int.Parse(Target.Value);
    }


    public abstract class BarDataPolicy<T, U> : DataPolicy<T, U> where T : BarBaseValue<U> where U : struct, INumber<U>
    {
        protected override U GetPropertyValue(T value) => Property switch
        {
            nameof(value.Min) => value.Min,
            nameof(value.Max) => value.Max,
            nameof(value.Mean) => value.Mean,
            nameof(value.LastValue) => value.LastValue,
            _ => default,
        };

        protected override Func<U, U, bool> GetOperation() => PolicyBuilder.BuilNumberOperation<U>(Action);
    }


    public sealed class DoubleBarDataPolicy : BarDataPolicy<DoubleBarValue, double>
    {
        protected override double GetConstTarget() => double.Parse(Target.Value);
    }
}
