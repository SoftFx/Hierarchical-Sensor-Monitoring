using HSMServer.Core.Model.Storages;

namespace HSMServer.Core.Model
{
    public sealed class DoubleValuesStorage : ValuesStorage<DoubleValue>
    {
        internal override DoubleValue CalculateStatistics(DoubleValue value) => StatisticsCalculation.CalculateEma<DoubleValue, double>(LastValue, value);

        internal override DoubleValue RecalculateStatistics(DoubleValue value) => StatisticsCalculation.RecalculateEma<DoubleValue, double>(LastValue, value);
    }
}
