using System;
using System.Numerics;

namespace HSMServer.Core.Model.Policies
{
    public abstract class SingleSensorPolicy<T, U> : Policy<T> where T : BaseValue<U>, new()
    {
        protected abstract PolicyCondition<T, U> BasePolicyCondition { get; }


        protected override AlertState GetState(BaseValue value) => AlertState.Build((T)value, _sensor);

        protected override PolicyCondition GetCondition(PolicyProperty property) => property switch
        {
            PolicyProperty.Value => BasePolicyCondition,
            _ => throw new NotImplementedException($"Not supported property {property} for {GetType().Name}"),
        };
    }


    public abstract class BarSensorPolicy<T, U> : Policy<T>
        where T : BarBaseValue<U>, new()
        where U : INumber<U>
    {
        protected abstract PolicyCondition<T, U> BasePolicyCondition { get; }


        protected override AlertState GetState(BaseValue value) => AlertState.Build((T)value, _sensor);

        protected override PolicyCondition GetCondition(PolicyProperty property) => property switch
        {
            PolicyProperty.Min or PolicyProperty.Max or PolicyProperty.Mean or PolicyProperty.LastValue => BasePolicyCondition,
            PolicyProperty.Count => new PolicyIntegerCondition<T>(),
            _ => throw new NotImplementedException($"Not supported property {property} for {GetType().Name}"),
        };
    }


    public sealed class IntegerPolicy : SingleSensorPolicy<IntegerValue, int>
    {
        protected override PolicyCondition<IntegerValue, int> BasePolicyCondition => new PolicyIntegerCondition<IntegerValue>();
    }


    public sealed class DoublePolicy : SingleSensorPolicy<DoubleValue, double>
    {
        protected override PolicyCondition<DoubleValue, double> BasePolicyCondition => new PolicyDoubleCondition<DoubleValue>();
    }


    public sealed class BooleanPolicy : SingleSensorPolicy<BooleanValue, bool>
    {
        protected override PolicyCondition<BooleanValue, bool> BasePolicyCondition => new PolicyBooleanCondition<BooleanValue>();
    }


    public sealed class StringPolicy : SingleSensorPolicy<StringValue, string>
    {
        protected override PolicyCondition<StringValue, string> BasePolicyCondition => new PolicyStringCondition<StringValue>();

        protected override PolicyCondition GetCondition(PolicyProperty property) => property switch
        {
            PolicyProperty.Length => new PolicyIntegerCondition<StringValue>(),
            _ => base.GetCondition(property)
        };
    }


    public sealed class TimeSpanPolicy : SingleSensorPolicy<TimeSpanValue, TimeSpan>
    {
        protected override PolicyCondition<TimeSpanValue, TimeSpan> BasePolicyCondition => new PolicyTimeSpanCondition<TimeSpanValue>();
    }


    public sealed class VersionPolicy : SingleSensorPolicy<VersionValue, Version>
    {
        protected override PolicyCondition<VersionValue, Version> BasePolicyCondition => new PolicyVersionCondition<VersionValue>();
    }


    public sealed class FilePolicy : SingleSensorPolicy<FileValue, byte[]>
    {
        protected override PolicyCondition<FileValue, byte[]> BasePolicyCondition => new PolicyByteArrayCondition<FileValue>();


        protected override PolicyCondition GetCondition(PolicyProperty property) => property switch
        {
            PolicyProperty.OriginalSize => new PolicyLongCondition<FileValue>(),
            _ => base.GetCondition(property)
        };
    }


    public sealed class IntegerBarPolicy : BarSensorPolicy<IntegerBarValue, int>
    {
        protected override PolicyCondition<IntegerBarValue, int> BasePolicyCondition => new PolicyIntegerCondition<IntegerBarValue>();
    }


    public sealed class DoubleBarPolicy : BarSensorPolicy<DoubleBarValue, double>
    {
        protected override PolicyCondition<DoubleBarValue, double> BasePolicyCondition => new PolicyDoubleCondition<DoubleBarValue>();
    }
}