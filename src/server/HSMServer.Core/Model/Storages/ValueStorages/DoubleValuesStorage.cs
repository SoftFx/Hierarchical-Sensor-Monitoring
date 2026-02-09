using HSMCommon.Model;
using HSMServer.Core.Model.Storages;
using System;


namespace HSMServer.Core.Model
{
    public sealed class DoubleValuesStorage : ValuesStorage<DoubleValue>
    {
        public DoubleValuesStorage(Func<BaseValue> getFirstValue, Func<BaseValue> getLastValue) : base(getFirstValue, getLastValue)
        {
        }

        internal override DoubleValue CalculateStatistics(DoubleValue value) => StatisticsCalculation.CalculateEma<DoubleValue, double>(LastValue, value);

        internal override DoubleValue RecalculateStatistics(DoubleValue value) => StatisticsCalculation.RecalculateEma<DoubleValue, double>(LastValue, value);
    }
}
