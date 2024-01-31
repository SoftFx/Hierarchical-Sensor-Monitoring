﻿using HSMServer.Core.Model.Storages;

namespace HSMServer.Core.Model
{
    public sealed class IntegerBarValuesStorage : BarValuesStorage<IntegerBarValue>
    {
        internal override IntegerBarValue CalculateStatistics(IntegerBarValue value) => StatisticsCalculation.CalculateBarEma<IntegerBarValue, int>(GetLastBar(value), value);

        internal override IntegerBarValue RecalculateStatistics(IntegerBarValue value) => StatisticsCalculation.RecalculateBarEma<IntegerBarValue, int>(GetLastBar(value), value);
    }
}