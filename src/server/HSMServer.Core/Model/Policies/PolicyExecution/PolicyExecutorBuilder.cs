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
                _ => throw new NotImplementedException($"{action} is not valid for number properties")
            };

        internal static Func<double?, double?, bool> GetNullableDoubleOperation(PolicyOperation action) =>
            action switch
            {
                PolicyOperation.LessThan => (double? src, double? target) => src < target,
                PolicyOperation.GreaterThan => (double? src, double? target) => src > target,
                PolicyOperation.LessThanOrEqual => (double? src, double? target) => src <= target,
                PolicyOperation.GreaterThanOrEqual => (double? src, double? target) => src >= target,
                PolicyOperation.Equal => (double? src, double? target) => src == target,
                PolicyOperation.NotEqual => (double? src, double? target) => src != target,
                _ => throw new NotImplementedException($"{action} is not valid for nullable double properties")
            };


        internal static Func<TimeSpan, TimeSpan, bool> GetTimeSpanOperation(PolicyOperation action) =>
            action switch
            {
                PolicyOperation.LessThan => (TimeSpan src, TimeSpan target) => src < target,
                PolicyOperation.GreaterThan => (TimeSpan src, TimeSpan target) => src > target,
                PolicyOperation.LessThanOrEqual => (TimeSpan src, TimeSpan target) => src <= target,
                PolicyOperation.GreaterThanOrEqual => (TimeSpan src, TimeSpan target) => src >= target,
                PolicyOperation.Equal => (TimeSpan src, TimeSpan target) => src == target,
                PolicyOperation.NotEqual => (TimeSpan src, TimeSpan target) => src != target,
                _ => throw new NotImplementedException($"{action} is not valid for TimeSpan {nameof(PolicyProperty.Value)} property")
            };


        internal static Func<Version, Version, bool> GetVersionOperation(PolicyOperation action) =>
            action switch
            {
                PolicyOperation.LessThan => (Version src, Version target) => src < target,
                PolicyOperation.GreaterThan => (Version src, Version target) => src > target,
                PolicyOperation.LessThanOrEqual => (Version src, Version target) => src <= target,
                PolicyOperation.GreaterThanOrEqual => (Version src, Version target) => src >= target,
                PolicyOperation.Equal => (Version src, Version target) => src == target,
                PolicyOperation.NotEqual => (Version src, Version target) => src != target,
                _ => throw new NotImplementedException($"{action} is not valid for Version {nameof(PolicyProperty.Value)} property")
            };


        internal static Func<string, string, bool> GetStringOperation(PolicyOperation? action)
        {
            static bool IsSuitableString(string target, Func<bool?> method) => target is null || (method() ?? false);

            return action switch
            {
                PolicyOperation.IsChanged => (string newVal, string oldVal) => oldVal != newVal,
                PolicyOperation.Equal => (string src, string target) => src == target,
                PolicyOperation.NotEqual => (string src, string target) => src != target,
                PolicyOperation.Contains => (string src, string target) => IsSuitableString(target, () => src?.Contains(target)),
                PolicyOperation.StartsWith => (string src, string target) => IsSuitableString(target, () => src?.StartsWith(target)),
                PolicyOperation.EndsWith => (string src, string target) => IsSuitableString(target, () => src?.EndsWith(target)),
                _ => throw new NotImplementedException($"{action} is not valid for string {nameof(PolicyProperty.Value)} or {nameof(PolicyProperty.Comment)} properties")
            };
        }


        internal static Func<SensorStatus?, SensorStatus?, bool> GetStatusOperation(PolicyOperation? action) =>
            action switch
            {
                PolicyOperation.IsChanged => IsChangedStatus,
                PolicyOperation.IsOk => (SensorStatus? newVal, SensorStatus? _) => newVal == SensorStatus.Ok,
                PolicyOperation.IsError => (SensorStatus? newVal, SensorStatus? _) => newVal == SensorStatus.Error,
                PolicyOperation.IsChangedToError => (SensorStatus? newVal, SensorStatus? oldVal) => IsChangedStatus(newVal, oldVal) && newVal == SensorStatus.Error,
                PolicyOperation.IsChangedToOk => (SensorStatus? newVal, SensorStatus? oldVal) => IsChangedStatus(newVal, oldVal) && newVal == SensorStatus.Ok,
                _ => throw new NotImplementedException($"{action} is not valid for {nameof(PolicyProperty.Status)} property")
            };


        internal static PolicyExecutor BuildExecutor<T, U>(PolicyProperty property) where T : BaseValue => property switch
        {
            PolicyProperty.Value or PolicyProperty.Min or PolicyProperty.Max or PolicyProperty.Mean or
            PolicyProperty.FirstValue or PolicyProperty.LastValue when typeof(U) == typeof(int) => new PolicyExecutorNumber<int>(property),

            PolicyProperty.Value or PolicyProperty.Min or PolicyProperty.Max or PolicyProperty.Mean or
            PolicyProperty.FirstValue or PolicyProperty.LastValue when typeof(U) == typeof(double) => new PolicyExecutorNumber<double>(property),

            PolicyProperty.EmaValue when typeof(T) == typeof(IntegerValue) => new PolicyExecutorNullableDouble<int>(property),
            PolicyProperty.EmaValue when typeof(T) == typeof(DoubleValue) || typeof(T) == typeof(CounterValue) => new PolicyExecutorNullableDouble<double>(property),

            PolicyProperty.EmaMin or PolicyProperty.EmaMax or PolicyProperty.EmaMean or
            PolicyProperty.EmaCount when typeof(T) == typeof(IntegerBarValue) => new PolicyExecutorNullableDouble<int>(property),
            PolicyProperty.EmaMin or PolicyProperty.EmaMax or PolicyProperty.EmaMean or
            PolicyProperty.EmaCount when typeof(T) == typeof(DoubleBarValue) => new PolicyExecutorNullableDouble<double>(property),

            PolicyProperty.Value when typeof(U) == typeof(TimeSpan) => new PolicyExecutorTimeSpan(),
            PolicyProperty.Value when typeof(U) == typeof(Version) => new PolicyExecutorVersion(),
            PolicyProperty.Value when typeof(U) == typeof(string) => new PolicyExecutorString(property),

            PolicyProperty.OriginalSize => new PolicyExecutorLong(property),
            PolicyProperty.Length or PolicyProperty.Count => new PolicyExecutorInt(property),

            PolicyProperty.Status => new PolicyExecutorStatus(),
            PolicyProperty.NewSensorData => new PolicyNewValueExecutor(),
            PolicyProperty.Comment => new PolicyExecutorString(property),

            _ => throw new NotImplementedException($"Unsupported policy property {property} with type {typeof(U).Name}"),
        };


        private static bool IsChangedStatus(SensorStatus? newVal, SensorStatus? oldVal)
        {
            var newValue = newVal.Value;

            return oldVal is not null && oldVal != newVal && !newValue.IsOfftime() && (!oldVal.Value.IsOfftime() || newValue.IsError());
        }
    }
}