using System;
using System.Numerics;

namespace HSMServer.Core.Model.Policies
{
    internal abstract class PolicyExecutorSimple<T> : PolicyExecutor<T>
    {
        protected override T GetCheckedValue(BaseValue value) => ((BaseValue<T>)value).Value;
    }


    internal class PolicyExecutorNumber<T> : PolicyExecutorSimple<T> where T : INumber<T>
    {
        private readonly Func<BaseValue, T> _getCheckedValue;


        internal PolicyExecutorNumber(PolicyProperty property)
        {
            _getCheckedValue = property switch
            {
                PolicyProperty.Value => v => ((BaseValue<T>)v).Value,
                PolicyProperty.Min => v => ((BarBaseValue<T>)v).Min,
                PolicyProperty.Max => v => ((BarBaseValue<T>)v).Max,
                PolicyProperty.Mean => v => ((BarBaseValue<T>)v).Mean,
                PolicyProperty.LastValue => v => ((BarBaseValue<T>)v).LastValue,
                _ => throw new NotImplementedException($"Invalid property {property} fro {nameof(PolicyExecutorNumber<T>)}")
            };
        }


        protected override Func<T, T, bool> GetTypedOperation(PolicyOperation operation) => PolicyExecutorBuilder.GetNumberOperation<T>(operation);

        protected override T GetCheckedValue(BaseValue value) => _getCheckedValue(value);
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