using HSMServer.Core.Model.Policies.Infrastructure;
using System;
using System.Numerics;
using System.Text;

namespace HSMServer.Core.Model.Policies
{
    public abstract class SingleSensorDataPolicy<T, U> : DataPolicy<T, U> where T : BaseValue<U>
    {
        protected override Func<T, U> GetProperty(string property) => DataPolicyBuilder.GetSingleProperty<T, U>(property);

        protected override string GetComment(T value, BaseSensorModel sensor) =>
            CustomCommentBuilder.GetSingleComment(value, sensor, Comment);
    }


    public abstract class BarSensorDataPolicy<T, U> : DataPolicy<T, U>
        where T : BarBaseValue<U>
        where U : struct, INumber<U>
    {
        protected override Func<T, U> GetProperty(string property) => DataPolicyBuilder.GetBarProperty<T, U>(property);

        protected override Func<U, U, bool> GetOperation(PolicyOperation operation) => DataPolicyBuilder.GetNumberOperation<U>(operation);

        protected override string GetComment(T value, BaseSensorModel sensor) =>
            CustomCommentBuilder.GetBarComment(value, sensor, Comment);
    }


    public sealed class IntegerDataPolicy : SingleSensorDataPolicy<IntegerValue, int>
    {
        protected override int GetConstTarget(string strValue) => int.Parse(strValue);

        protected override Func<int, int, bool> GetOperation(PolicyOperation operation) => DataPolicyBuilder.GetNumberOperation<int>(operation);
    }


    public sealed class DoubleDataPolicy : SingleSensorDataPolicy<DoubleValue, double>
    {
        protected override double GetConstTarget(string strValue) => double.Parse(strValue);

        protected override Func<double, double, bool> GetOperation(PolicyOperation operation) => DataPolicyBuilder.GetNumberOperation<double>(operation);
    }


    public sealed class BooleanDataPolicy : SingleSensorDataPolicy<BooleanValue, bool>
    {
        protected override bool GetConstTarget(string strValue) => bool.Parse(strValue);

        protected override Func<bool, bool, bool> GetOperation(PolicyOperation operation) => (bool src, bool target) => true;
    }


    public sealed class StringDataPolicy : SingleSensorDataPolicy<StringValue, string>
    {
        protected override string GetConstTarget(string strValue) => strValue;

        protected override Func<string, string, bool> GetOperation(PolicyOperation operation) => (string src, string target) => true;
    }


    public sealed class TimeSpanDataPolicy : SingleSensorDataPolicy<TimeSpanValue, TimeSpan>
    {
        protected override TimeSpan GetConstTarget(string strValue) => TimeSpan.Parse(strValue);

        protected override Func<TimeSpan, TimeSpan, bool> GetOperation(PolicyOperation operation) => (TimeSpan src, TimeSpan target) => true;
    }


    public sealed class VersionDataPolicy : SingleSensorDataPolicy<VersionValue, Version>
    {
        protected override Version GetConstTarget(string strValue) => Version.Parse(strValue);

        protected override Func<Version, Version, bool> GetOperation(PolicyOperation operation) => (Version src, Version target) => true;
    }


    public sealed class FileDataPolicy : SingleSensorDataPolicy<FileValue, byte[]>
    {
        protected override byte[] GetConstTarget(string strValue) => Encoding.UTF8.GetBytes(strValue);

        protected override Func<byte[], byte[], bool> GetOperation(PolicyOperation operation) => (byte[] src, byte[] target) => true;
    }


    public sealed class IntegerBarDataPolicy : BarSensorDataPolicy<IntegerBarValue, int>
    {
        protected override int GetConstTarget(string strValue) => int.Parse(strValue);
    }


    public sealed class DoubleBarDataPolicy : BarSensorDataPolicy<DoubleBarValue, double>
    {
        protected override double GetConstTarget(string strValue) => double.Parse(strValue);
    }
}
