using System;
using System.Numerics;

namespace HSMServer.Core.Model.Policies
{
    internal static class PolicyExecutorBuilder
    {
        internal static Func<T, T, bool> GetNumberOperation<T>(PolicyOperation action) where T : INumber<T> =>
            action switch
            {
                PolicyOperation.LessThan => (T src, T target) => src < target,
                PolicyOperation.GreaterThan => (T src, T target) => src > target,
                PolicyOperation.LessThanOrEqual => (T src, T target) => src <= target,
                PolicyOperation.GreaterThanOrEqual => (T src, T target) => src >= target,
                PolicyOperation.Equal => (T src, T target) => src == target,
                PolicyOperation.NotEqual => (T src, T target) => src != target,
                _ => throw new NotImplementedException()
            };

        internal static Func<SensorStatus, SensorStatus, bool> GetStatusOperation(PolicyOperation action) =>
            action switch
            {
                PolicyOperation.Change => (SensorStatus oldVal, SensorStatus newVal) => oldVal != newVal,
                PolicyOperation.IsOk => (SensorStatus _, SensorStatus newVal) => newVal == SensorStatus.Error,
                PolicyOperation.IsError => (SensorStatus _, SensorStatus newVal) => newVal == SensorStatus.Ok,
                _ => throw new NotImplementedException()
            };

        internal static PolicyExecutor BuildExecutor<U>(string property) => property switch
        {
            nameof(BaseValue<U>.Value) or nameof(BarBaseValue<int>.Min) or nameof(BarBaseValue<int>.Max) or
            nameof(BarBaseValue<int>.Mean) or nameof(BarBaseValue<int>.LastValue) when typeof(U) == typeof(int) => new PolicyExecutorInt(property),

            nameof(BaseValue<U>.Value) or nameof(BarBaseValue<int>.Min) or nameof(BarBaseValue<int>.Max) or
            nameof(BarBaseValue<int>.Mean) or nameof(BarBaseValue<int>.LastValue) when typeof(U) == typeof(double) => new PolicyExecutorDouble(property),

            nameof(BaseValue.Status) => new PolicyExecutorStatus(),

            _ => throw new NotImplementedException($"Unsupported policy property {property} with type {typeof(U).Name}"),
        };
    }
}