using System;
using System.ComponentModel.DataAnnotations;
using System.Numerics;

namespace HSMServer.Core.Model.Policies
{
    public enum PolicyOperation : byte
    {
        [Display(Name = "<=")]
        LessThanOrEqual,
        [Display(Name = "<")]
        LessThan,
        [Display(Name = ">")]
        GreaterThan,
        [Display(Name = ">=")]
        GreaterThanOrEqual,
        [Display(Name = "==")]
        Equal,
        [Display(Name = "!=")]
        NotEqual,
    }

    public enum TargetType : byte
    {
        Const,
        Sensor,
    }


    public sealed record TargetValue(TargetType Type, string Value);


    internal static class DataPolicyBuilder
    {
        internal static Func<U, U, bool> GetNumberOperation<U>(PolicyOperation action) where U : INumber<U> =>
            action switch
            {
                PolicyOperation.LessThan => (U src, U target) => src < target,
                PolicyOperation.GreaterThan => (U src, U target) => src > target,
                PolicyOperation.LessThanOrEqual => (U src, U target) => src <= target,
                PolicyOperation.GreaterThanOrEqual => (U src, U target) => src >= target,
                PolicyOperation.Equal => (U src, U target) => src == target,
                PolicyOperation.NotEqual => (U src, U target) => src != target,
                _ => (_, _) => false,
            };

        internal static Func<T, U> GetSingleProperty<T, U>(string property) where T : BaseValue<U> =>
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