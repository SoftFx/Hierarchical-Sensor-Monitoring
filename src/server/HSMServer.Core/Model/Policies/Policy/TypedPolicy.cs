using System;
using System.Numerics;
using System.Text;

namespace HSMServer.Core.Model.Policies
{
    public abstract class SingleSensorPolicy<T, U> : Policy<T, U> where T : BaseValue<U>, new()
    {
        protected override AlertState GetState(BaseValue value) => AlertState.Build((T)value, _sensor);
    }


    public abstract class BarSensorPolicy<T, U> : Policy<T, U>
        where T : BarBaseValue<U>, new()
        where U : INumber<U>
    {
        protected override AlertState GetState(BaseValue value) => AlertState.Build((T)value, _sensor);
    }


    public sealed class IntegerPolicy : SingleSensorPolicy<IntegerValue, int>
    {
        protected override int GetConstTarget(string strValue) => int.Parse(strValue);
    }


    public sealed class DoublePolicy : SingleSensorPolicy<DoubleValue, double>
    {
        protected override double GetConstTarget(string strValue) => double.Parse(strValue);
    }


    public sealed class BooleanPolicy : SingleSensorPolicy<BooleanValue, bool>
    {
        protected override bool GetConstTarget(string strValue) => bool.Parse(strValue);
    }


    public sealed class StringPolicy : SingleSensorPolicy<StringValue, string>
    {
        protected override string GetConstTarget(string strValue) => strValue;
    }


    public sealed class TimeSpanPolicy : SingleSensorPolicy<TimeSpanValue, TimeSpan>
    {
        protected override TimeSpan GetConstTarget(string strValue) => TimeSpan.Parse(strValue);
    }


    public sealed class VersionPolicy : SingleSensorPolicy<VersionValue, Version>
    {
        protected override Version GetConstTarget(string strValue) => Version.Parse(strValue);
    }


    public sealed class FilePolicy : SingleSensorPolicy<FileValue, byte[]>
    {
        protected override byte[] GetConstTarget(string strValue) => Encoding.UTF8.GetBytes(strValue);
    }


    public sealed class IntegerBarPolicy : BarSensorPolicy<IntegerBarValue, int>
    {
        protected override int GetConstTarget(string strValue) => int.Parse(strValue);
    }


    public sealed class DoubleBarPolicy : BarSensorPolicy<DoubleBarValue, double>
    {
        protected override double GetConstTarget(string strValue) => double.Parse(strValue);
    }
}