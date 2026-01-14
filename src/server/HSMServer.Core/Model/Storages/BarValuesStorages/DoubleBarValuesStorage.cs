using HSMCommon.Model;
using HSMServer.Core.Model.Storages;


namespace HSMServer.Core.Model
{
    public sealed class DoubleBarValuesStorage : BarValuesStorage<DoubleBarValue>
    {
        internal override DoubleBarValue CalculateStatistics(DoubleBarValue value) => StatisticsCalculation.CalculateBarEma<DoubleBarValue, double>(GetLastBar(value), value);

        internal override DoubleBarValue RecalculateStatistics(DoubleBarValue value) => StatisticsCalculation.RecalculateBarEma<DoubleBarValue, double>(GetLastBar(value), value);
    }
}