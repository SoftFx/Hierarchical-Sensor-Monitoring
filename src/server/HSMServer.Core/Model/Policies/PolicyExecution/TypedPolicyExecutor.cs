using System;
using System.Numerics;

namespace HSMServer.Core.Model.Policies
{
    internal abstract class PolicyExecutorSimple<T> : PolicyExecutor<T>
    {
        protected override T GetCheckedValue(BaseValue value) => ((BaseValue<T>)value).Value;
    }


    internal abstract class PolicyExecutorNumberBase<T> : PolicyExecutorSimple<T> where T : INumber<T>
    {
        protected Func<BaseValue, T> _getCheckedValue;


        protected override Func<T, T, bool> GetTypedOperation(PolicyOperation operation) => PolicyExecutorBuilder.GetNumberOperation<T>(operation);

        protected override T GetCheckedValue(BaseValue value) => _getCheckedValue(value);
    }


    internal sealed class PolicyExecutorNumber<T> : PolicyExecutorNumberBase<T> where T : INumber<T>
    {
        internal PolicyExecutorNumber(PolicyProperty property)
        {
            _getCheckedValue = property switch
            {
                PolicyProperty.Value => v => ((BaseValue<T>)v).Value,
                PolicyProperty.Min => v => ((BarBaseValue<T>)v).Min,
                PolicyProperty.Max => v => ((BarBaseValue<T>)v).Max,
                PolicyProperty.Mean => v => ((BarBaseValue<T>)v).Mean,
                PolicyProperty.LastValue => v => ((BarBaseValue<T>)v).LastValue,
                _ => throw new NotImplementedException($"Invalid property {property} for {nameof(PolicyExecutorNumber<T>)}")
            };
        }
    }


    internal sealed class PolicyExecutorLong : PolicyExecutorNumberBase<long>
    {
        internal PolicyExecutorLong(PolicyProperty property)
        {
            _getCheckedValue = property switch
            {
                PolicyProperty.Count => v => ((BarBaseValue)v).Count,
                PolicyProperty.OriginalSize => v => ((FileValue)v).OriginalSize,
                _ => throw new NotImplementedException($"Invalid property {property} for {nameof(PolicyExecutorLong)}")
            };
        }
    }

    internal sealed class PolicyExecutorDouble : PolicyExecutorNumberBase<double>
    {
        internal PolicyExecutorDouble(PolicyProperty property)
        {
            _getCheckedValue = property switch
            {
                PolicyProperty.Count => v => ((BarBaseValue)v).Count,
                _ => throw new NotImplementedException($"Invalid property {property} for {nameof(PolicyExecutorDouble)}")
            };
        }
    }


    internal sealed class PolicyExecutorString : PolicyExecutorSimple<string>
    {
        protected override Func<string, string, bool> GetTypedOperation(PolicyOperation operation) => PolicyExecutorBuilder.GetStringOperation(operation);

        protected override string GetCheckedValue(BaseValue value) => value?.Comment;
    }


    internal sealed class PolicyExecutorTimeSpan : PolicyExecutorSimple<TimeSpan>
    {
        protected override Func<TimeSpan, TimeSpan, bool> GetTypedOperation(PolicyOperation operation) => PolicyExecutorBuilder.GetTimeSpanOperation(operation);
    }


    internal sealed class PolicyExecutorStatus : PolicyExecutor<SensorStatus?>
    {
        protected override Func<SensorStatus?, SensorStatus?, bool> GetTypedOperation(PolicyOperation operation) => PolicyExecutorBuilder.GetStatusOperation(operation);

        protected override SensorStatus? GetCheckedValue(BaseValue value) => value?.Status;
    }
}