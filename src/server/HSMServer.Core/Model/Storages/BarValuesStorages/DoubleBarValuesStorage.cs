using HSMServer.Core.Model.Storages;

namespace HSMServer.Core.Model
{
    public sealed class DoubleBarValuesStorage : BarValuesStorage<DoubleBarValue>
    {
        internal override DoubleBarValue CalculateStatistics(DoubleBarValue value) => StatisticsCalculation.CalculateBarEma<DoubleBarValue, double>(LastValue, value);
    }
}
