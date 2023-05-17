using System;
using System.Numerics;

namespace HSMServer.Core.Model.Policies
{
    public abstract class SingleSensorDataPolicy<T, U> : DataPolicy<T, U> where T : BaseValue<U>
    {
        protected override Func<T, U> GetProperty(string property) => DataPolicyBuilder.GetSingleProperty<T, U>(property);
    }


    public abstract class BarSensorDataPolicy<T, U> : DataPolicy<T, U>
        where T : BarBaseValue<U>
        where U : struct, INumber<U>
    {
        protected override Func<T, U> GetProperty(string property) => DataPolicyBuilder.GetBarProperty<T, U>(property);

        protected override Func<U, U, bool> GetOperation(PolicyOperation operation) => DataPolicyBuilder.GetNumberOperation<U>(operation);
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


    public sealed class IntegerBarDataPolicy : BarSensorDataPolicy<IntegerBarValue, int>
    {
        protected override int GetConstTarget(string strValue) => int.Parse(strValue);
    }


    public sealed class DoubleBarDataPolicy : BarSensorDataPolicy<DoubleBarValue, double>
    {
        protected override double GetConstTarget(string strValue) => double.Parse(strValue);
    }
}
