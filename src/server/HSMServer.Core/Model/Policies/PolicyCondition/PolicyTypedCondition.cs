using System;
using System.Text;
using HSMCommon.Model;

namespace HSMServer.Core.Model.Policies
{
    internal class PolicyLongCondition<T> : PolicyCondition<T, long> where T : BaseValue
    {
        internal override long TargetConstConverter(string str) => long.Parse(str);
    }

    internal class PolicyIntegerCondition<T> : PolicyCondition<T, int> where T : BaseValue
    {
        internal override int TargetConstConverter(string str) => int.Parse(str);
    }


    internal class PolicyDoubleCondition<T> : PolicyCondition<T, double> where T : BaseValue
    {
        internal override double TargetConstConverter(string str) => double.Parse(str);
    }

    internal class PolicyNullableDoubleCondition<T> : PolicyCondition<T, double?> where T : BaseValue
    {
        internal override double? TargetConstConverter(string str) => !string.IsNullOrEmpty(str) ? double.Parse(str) : null;
    }


    internal class PolicyBooleanCondition<T> : PolicyCondition<T, bool> where T : BaseValue
    {
        internal override bool TargetConstConverter(string str) => bool.Parse(str);
    }


    internal class PolicyStringCondition<T> : PolicyCondition<T, string> where T : BaseValue
    {
        internal override string TargetConstConverter(string str) => str;
    }


    internal class PolicyTimeSpanCondition<T> : PolicyCondition<T, TimeSpan> where T : BaseValue
    {
        internal override TimeSpan TargetConstConverter(string str) => TimeSpan.Parse(str);
    }


    internal class PolicyVersionCondition<T> : PolicyCondition<T, Version> where T : BaseValue
    {
        internal override Version TargetConstConverter(string str) => Version.Parse(str);
    }


    internal class PolicyByteArrayCondition<T> : PolicyCondition<T, byte[]> where T : BaseValue
    {
        internal override byte[] TargetConstConverter(string str) => Encoding.UTF8.GetBytes(str);
    }
}