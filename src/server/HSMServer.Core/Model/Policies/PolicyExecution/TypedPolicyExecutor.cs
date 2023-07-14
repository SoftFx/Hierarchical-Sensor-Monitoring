using System;

namespace HSMServer.Core.Model.Policies
{
    internal abstract class PolicyExecutorSimple<T> : PolicyExecutor<T>
    {
        protected override T GetCheckedValue(BaseValue value) => ((BaseValue<T>)value).Value;
    }


    internal class PolicyExecutorInt : PolicyExecutorSimple<int>
    {
        private readonly Func<BaseValue, int> _getCheckedValue;


        internal PolicyExecutorInt(string property)
        {
            _getCheckedValue = property switch
            {
                nameof(BaseValue<int>.Value) => v => ((BaseValue<int>)v).Value,
                nameof(BarBaseValue<int>.Min) => v => ((BarBaseValue<int>)v).Min,
                nameof(BarBaseValue<int>.Max) => v => ((BarBaseValue<int>)v).Max,
                nameof(BarBaseValue<int>.Mean) => v => ((BarBaseValue<int>)v).Mean,
                nameof(BarBaseValue<int>.LastValue) => v => ((BarBaseValue<int>)v).LastValue,
                _ => throw new NotImplementedException($"Invalid property {property} fro {nameof(PolicyExecutorInt)}")
            };
        }


        protected override Func<int, int, bool> GetTypedOperation(PolicyOperation operation) => PolicyExecutorBuilder.GetNumberOperation<int>(operation);

        protected override int GetCheckedValue(BaseValue value) => _getCheckedValue(value);
    }


    internal class PolicyExecutorDouble : PolicyExecutorSimple<double>
    {
        private readonly Func<BaseValue, double> _getCheckedValue;


        internal PolicyExecutorDouble(string property)
        {
            _getCheckedValue = property switch
            {
                nameof(BaseValue<double>.Value) => v => ((BaseValue<double>)v).Value,
                nameof(BarBaseValue<double>.Min) => v => ((BarBaseValue<double>)v).Min,
                nameof(BarBaseValue<double>.Max) => v => ((BarBaseValue<double>)v).Max,
                nameof(BarBaseValue<double>.Mean) => v => ((BarBaseValue<double>)v).Mean,
                nameof(BarBaseValue<double>.LastValue) => v => ((BarBaseValue<double>)v).LastValue,
                _ => throw new NotImplementedException($"Invalid property {property} fro {nameof(PolicyExecutorDouble)}")
            };
        }


        protected override Func<double, double, bool> GetTypedOperation(PolicyOperation operation) => PolicyExecutorBuilder.GetNumberOperation<double>(operation);

        protected override double GetCheckedValue(BaseValue value) => _getCheckedValue(value);
    }


    internal class PolicyExecutorString : PolicyExecutorSimple<string>
    {
        protected override Func<string, string, bool> GetTypedOperation(PolicyOperation operation) => throw new NotImplementedException();
    }


    internal class PolicyExecutorStatus : PolicyExecutor<SensorStatus?>
    {
        protected override Func<SensorStatus?, SensorStatus?, bool> GetTypedOperation(PolicyOperation operation) => PolicyExecutorBuilder.GetStatusOperation(operation);

        protected override SensorStatus? GetCheckedValue(BaseValue value) => value?.Status;
    }
}