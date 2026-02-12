using HSMCommon.Model;
using HSMServer.Core.Model.Storages;
using System;

namespace HSMServer.Core.Model
{
    public sealed class IntegerBarValuesStorage : BarValuesStorage<IntegerBarValue>
    {
        public IntegerBarValuesStorage(Func<BaseValue> getFirstValue, Func<BaseValue> getLastValue) : base(getFirstValue, getLastValue)
        {
        }

        internal override IntegerBarValue CalculateStatistics(IntegerBarValue value) => StatisticsCalculation.CalculateBarEma<IntegerBarValue, int>(GetLastBar(value), value);

        internal override IntegerBarValue RecalculateStatistics(IntegerBarValue value) => StatisticsCalculation.RecalculateBarEma<IntegerBarValue, int>(GetLastBar(value), value);
    }
}