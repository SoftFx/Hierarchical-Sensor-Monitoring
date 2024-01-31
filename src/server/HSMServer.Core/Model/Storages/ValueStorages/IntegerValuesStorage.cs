﻿using HSMServer.Core.Model.Storages;

namespace HSMServer.Core.Model
{
    public sealed class IntegerValuesStorage : ValuesStorage<IntegerValue>
    {
        internal override IntegerValue CalculateStatistics(IntegerValue value) => StatisticsCalculation.CalculateEma<IntegerValue, int>(LastValue, value);

        internal override IntegerValue RecalculateStatistics(IntegerValue value) => StatisticsCalculation.RecalculateEma<IntegerValue, int>(LastValue, value);
    }
}