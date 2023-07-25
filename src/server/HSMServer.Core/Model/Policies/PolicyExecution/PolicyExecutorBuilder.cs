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


        internal static Func<SensorStatus?, SensorStatus?, bool> GetStatusOperation(PolicyOperation? action) =>
            action switch
            {
                PolicyOperation.IsChanged => (SensorStatus? newVal, SensorStatus? oldVal) => oldVal is not null && oldVal != newVal,
                PolicyOperation.IsOk => (SensorStatus? newVal, SensorStatus? _) => newVal == SensorStatus.Error,
                PolicyOperation.IsError => (SensorStatus? newVal, SensorStatus? _) => newVal == SensorStatus.Ok,
                _ => throw new NotImplementedException()
            };


        internal static PolicyExecutor BuildExecutor<U>(PolicyProperty property) => property switch
        {
            PolicyProperty.Value or PolicyProperty.Min or PolicyProperty.Max or PolicyProperty.Mean or
            PolicyProperty.LastValue when typeof(U) == typeof(int) => new PolicyExecutorNumber<int>(property),

            PolicyProperty.Value or PolicyProperty.Min or PolicyProperty.Max or
            PolicyProperty.Mean or PolicyProperty.LastValue when typeof(U) == typeof(double) => new PolicyExecutorNumber<double>(property),

            PolicyProperty.Status => new PolicyExecutorStatus(),

            _ => throw new NotImplementedException($"Unsupported policy property {property} with type {typeof(U).Name}"),
        };
    }
}