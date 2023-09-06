using System;
using System.Text;

namespace HSMServer.Core.Model.Policies
{
    internal class PolicyLongCondition<T> : PolicyCondition<T, long> where T : BaseValue
    {
        internal override long ConstTargetValueConverter(string str) => long.Parse(str);
    }

    internal class PolicyIntegerCondition<T> : PolicyCondition<T, int> where T : BaseValue
    {
        internal override int ConstTargetValueConverter(string str) => int.Parse(str);
    }


    internal class PolicyDoubleCondition<T> : PolicyCondition<T, double> where T : BaseValue
    {
        internal override double ConstTargetValueConverter(string str) => double.Parse(str);
    }


    internal class PolicyBooleanCondition<T> : PolicyCondition<T, bool> where T : BaseValue
    {
        internal override bool ConstTargetValueConverter(string str) => bool.Parse(str);
    }


    internal class PolicyStringCondition<T> : PolicyCondition<T, string> where T : BaseValue
    {
        internal override string ConstTargetValueConverter(string str) => str;
    }


    internal class PolicyTimeSpanCondition<T> : PolicyCondition<T, TimeSpan> where T : BaseValue
    {
        internal override TimeSpan ConstTargetValueConverter(string str) => TimeSpan.Parse(str);
    }


    internal class PolicyVersionCondition<T> : PolicyCondition<T, Version> where T : BaseValue
    {
        internal override Version ConstTargetValueConverter(string str) => Version.Parse(str);
    }


    internal class PolicyByteArrayCondition<T> : PolicyCondition<T, byte[]> where T : BaseValue
    {
        internal override byte[] ConstTargetValueConverter(string str) => Encoding.UTF8.GetBytes(str);
    }
}