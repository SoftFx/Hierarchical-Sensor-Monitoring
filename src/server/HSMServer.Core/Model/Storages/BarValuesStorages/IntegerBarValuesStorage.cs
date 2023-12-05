using HSMServer.Core.Model.Storages;

namespace HSMServer.Core.Model
{
    public sealed class IntegerBarValuesStorage : BarValuesStorage<IntegerBarValue>
    {
        internal override IntegerBarValue CalculateStatistics(IntegerBarValue value) => StatisticsCalculation.CalculateBarEma<IntegerBarValue, int>(LastValue, value);
    }
}
