using HSMCommon.Model;
using System;


namespace HSMServer.Core.Model.Storages.ValueStorages
{
    internal sealed class RateValuesStorage : ValuesStorage<RateValue>
    {
        internal override RateValue CalculateStatistics(RateValue value) => StatisticsCalculation.CalculateEma<RateValue, double>(LastValue, value);

        internal override RateValue RecalculateStatistics(RateValue value) => StatisticsCalculation.RecalculateEma<RateValue, double>(LastValue, value);

    }
}