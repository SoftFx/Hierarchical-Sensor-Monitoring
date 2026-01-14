using System;
using System.Numerics;
using HSMCommon.Model;

namespace HSMServer.Core.Model.Policies
{
    internal abstract class PolicyExecutorSimple<T> : PolicyExecutor<T>
    {
        protected override T GetCheckedValue(BaseValue value) => ((BaseValue<T>)value).Value;
    }


    internal abstract class PolicyMultiplePropertyExecutor<T> : PolicyExecutorSimple<T>
    {
        protected Func<BaseValue, T> _getCheckedValue;

        protected override T GetCheckedValue(BaseValue value) => _getCheckedValue(value);
    }


    internal abstract class PolicyExecutorNumberBase<T> : PolicyMultiplePropertyExecutor<T> where T : INumber<T>
    {
        protected override Func<T, T, bool> GetTypedOperation(PolicyOperation operation) => PolicyExecutorBuilder.GetNumberOperation<T>(operation);
    }


    internal sealed class PolicyExecutorNumber<T> : PolicyExecutorNumberBase<T> where T : struct, INumber<T>
    {
        internal PolicyExecutorNumber(PolicyProperty property)
        {
            _getCheckedValue = property switch
            {
                PolicyProperty.Value => v => ((BaseValue<T>)v).Value,
                PolicyProperty.Min => v => ((BarBaseValue<T>)v).Min,
                PolicyProperty.Max => v => ((BarBaseValue<T>)v).Max,
                PolicyProperty.Mean => v => ((BarBaseValue<T>)v).Mean,
                PolicyProperty.FirstValue => v => ((BarBaseValue<T>)v).FirstValue ?? ((BarBaseValue<T>)v).Min,
                PolicyProperty.LastValue => v => ((BarBaseValue<T>)v).LastValue,
                _ => throw new NotImplementedException($"Invalid property {property} for {nameof(PolicyExecutorNumber<T>)}")
            };
        }
    }


    internal sealed class PolicyExecutorNullableDouble<T> : PolicyMultiplePropertyExecutor<double?> where T : struct, INumber<T>
    {
        internal PolicyExecutorNullableDouble(PolicyProperty property)
        {
            _getCheckedValue = property switch
            {
                PolicyProperty.EmaValue => v => ((BaseValue<T>)v).EmaValue,
                PolicyProperty.EmaMin => v => ((BarBaseValue<T>)v).EmaMin,
                PolicyProperty.EmaMax => v => ((BarBaseValue<T>)v).EmaMax,
                PolicyProperty.EmaMean => v => ((BarBaseValue<T>)v).EmaMean,
                PolicyProperty.EmaCount => v => ((BarBaseValue<T>)v).EmaCount,
                _ => throw new NotImplementedException($"Invalid property {property} for {nameof(PolicyExecutorNullableDouble<T>)}")
            };
        }

        protected override Func<double?, double?, bool> GetTypedOperation(PolicyOperation operation) => PolicyExecutorBuilder.GetNullableDoubleOperation(operation);
    }


    internal sealed class PolicyExecutorLong : PolicyExecutorNumberBase<long>
    {
        internal PolicyExecutorLong(PolicyProperty property)
        {
            _getCheckedValue = property switch
            {
                PolicyProperty.OriginalSize => v => ((FileValue)v).OriginalSize,
                _ => throw new NotImplementedException($"Invalid property {property} for {nameof(PolicyExecutorLong)}")
            };
        }
    }


    internal sealed class PolicyExecutorInt : PolicyExecutorNumberBase<int>
    {
        internal PolicyExecutorInt(PolicyProperty property)
        {
            _getCheckedValue = property switch
            {
                PolicyProperty.Count => v => ((BarBaseValue)v).Count,
                PolicyProperty.Length => v => ((StringValue)v).Value.Length,
                _ => throw new NotImplementedException($"Invalid property {property} for {nameof(PolicyExecutorInt)}")
            };
        }
    }


    internal sealed class PolicyExecutorString : PolicyMultiplePropertyExecutor<string>
    {
        internal PolicyExecutorString(PolicyProperty property)
        {
            _getCheckedValue = property switch
            {
                PolicyProperty.Comment => v => v?.Comment,
                PolicyProperty.Value => v => ((StringValue)v)?.Value,
                _ => throw new NotImplementedException($"Invalid property {property} for {nameof(PolicyExecutorString)}")
            };
        }


        protected override Func<string, string, bool> GetTypedOperation(PolicyOperation operation) => PolicyExecutorBuilder.GetStringOperation(operation);
    }


    internal sealed class PolicyExecutorVersion : PolicyExecutorSimple<Version>
    {
        protected override Func<Version, Version, bool> GetTypedOperation(PolicyOperation operation) => PolicyExecutorBuilder.GetVersionOperation(operation);
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


    internal sealed class PolicyNewValueExecutor : PolicyExecutor<BaseValue>
    {
        protected override BaseValue GetCheckedValue(BaseValue value) => value;

        protected override Func<BaseValue, BaseValue, bool> GetTypedOperation(PolicyOperation operation) => (_, _) => true;
    }
}