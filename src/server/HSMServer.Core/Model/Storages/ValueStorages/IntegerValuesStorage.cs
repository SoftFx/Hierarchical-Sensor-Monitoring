using HSMCommon.Model;
using HSMServer.Core.Model.Storages;
using System;

namespace HSMServer.Core.Model
{
    public sealed class IntegerValuesStorage : ValuesStorage<IntegerValue>
    {
        public IntegerValuesStorage(Func<BaseValue> getFirstValue, Func<BaseValue> getLastValue) : base(getFirstValue, getLastValue)
        {
        }

        internal override IntegerValue CalculateStatistics(IntegerValue value) => StatisticsCalculation.CalculateEma<IntegerValue, int>(LastValue, value);

        internal override IntegerValue RecalculateStatistics(IntegerValue value) => StatisticsCalculation.RecalculateEma<IntegerValue, int>(LastValue, value);
    }
}