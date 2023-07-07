using System;
using System.Numerics;
using System.Text;

namespace HSMServer.Core.Model.Policies
{
    public abstract class SingleSensorPolicy<T, U> : Policy<T, U> where T : BaseValue<U>, new()
    {
        protected override Func<T, U> GetProperty(string property) => PolicyBuilder.GetSingleProperty<T, U>(property);

        protected override AlertState GetState(T value, BaseSensorModel sensor) => FillPolicyState(AlertState.Build(value, sensor));
    }


    public abstract class BarSensorPolicy<T, U> : Policy<T, U>
        where T : BarBaseValue<U>, new()
        where U : struct, INumber<U>
    {
        protected override Func<T, U> GetProperty(string property) => PolicyBuilder.GetBarProperty<T, U>(property);

        protected override Func<U, U, bool> GetOperation(PolicyOperation operation) => PolicyBuilder.GetNumberOperation<U>(operation);

        protected override AlertState GetState(T value, BaseSensorModel sensor) => FillPolicyState(AlertState.Build(value, sensor));
    }


    public sealed class IntegerPolicy : SingleSensorPolicy<IntegerValue, int>
    {
        protected override int GetConstTarget(string strValue) => int.Parse(strValue);

        protected override Func<int, int, bool> GetOperation(PolicyOperation operation) => PolicyBuilder.GetNumberOperation<int>(operation);
    }


    public sealed class DoublePolicy : SingleSensorPolicy<DoubleValue, double>
    {
        protected override double GetConstTarget(string strValue) => double.Parse(strValue);

        protected override Func<double, double, bool> GetOperation(PolicyOperation operation) => PolicyBuilder.GetNumberOperation<double>(operation);
    }


    public sealed class BooleanPolicy : SingleSensorPolicy<BooleanValue, bool>
    {
        protected override bool GetConstTarget(string strValue) => bool.Parse(strValue);

        protected override Func<bool, bool, bool> GetOperation(PolicyOperation operation) => (bool src, bool target) => true;
    }


    public sealed class StringPolicy : SingleSensorPolicy<StringValue, string>
    {
        protected override string GetConstTarget(string strValue) => strValue;

        protected override Func<string, string, bool> GetOperation(PolicyOperation operation) => (string src, string target) => true;
    }


    public sealed class TimeSpanPolicy : SingleSensorPolicy<TimeSpanValue, TimeSpan>
    {
        protected override TimeSpan GetConstTarget(string strValue) => TimeSpan.Parse(strValue);

        protected override Func<TimeSpan, TimeSpan, bool> GetOperation(PolicyOperation operation) => (TimeSpan src, TimeSpan target) => true;
    }


    public sealed class VersionPolicy : SingleSensorPolicy<VersionValue, Version>
    {
        protected override Version GetConstTarget(string strValue) => Version.Parse(strValue);

        protected override Func<Version, Version, bool> GetOperation(PolicyOperation operation) => (Version src, Version target) => true;
    }


    public sealed class FilePolicy : SingleSensorPolicy<FileValue, byte[]>
    {
        protected override byte[] GetConstTarget(string strValue) => Encoding.UTF8.GetBytes(strValue);

        protected override Func<byte[], byte[], bool> GetOperation(PolicyOperation operation) => (byte[] src, byte[] target) => true;
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