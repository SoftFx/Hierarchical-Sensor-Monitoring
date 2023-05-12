using System;
using System.Numerics;

namespace HSMServer.Core.Model.Policies
{
    internal static class PolicyBuilder
    {
        internal static Func<U, U, bool> BuilNumberOperation<U>(Operation action) where U : INumber<U> =>
            action switch
            {
                Operation.LessThan => (U src, U target) => src < target,
                Operation.GreaterThan => (U src, U target) => src > target,
                Operation.LessThanOrEqual => (U src, U target) => src <= target,
                Operation.GreaterThanOrEqual => (U src, U target) => src >= target,
                Operation.Equal => (U src, U target) => src == target,
                Operation.NotEqual => (U src, U target) => src != target,
                _ => (_, _) => false,
            };

        internal static Func<T, U> GetSimpleProperty<T, U>(string property) where T : BaseValue<U> =>
            property switch
            {
                nameof(BaseValue<U>.Value) => (T value) => value.Value,
                _ => default,
            };

        internal static Func<T, U> GetBarProperty<T, U>(string property) where T : BarBaseValue<U> where U : struct =>
             property switch
             {
                 nameof(BarBaseValue<U>.Min) => (T value) => value.Min,
                 nameof(BarBaseValue<U>.Max) => (T value) => value.Max,
                 nameof(BarBaseValue<U>.Mean) => (T value) => value.Mean,
                 nameof(BarBaseValue<U>.LastValue) => (T value) => value.LastValue,
                 _ => default,
             };
    }
}
