using HSMServer.Core.Model.Storages;

namespace HSMServer.Core.Model
{
    public sealed class DoubleValuesStorage : ValuesStorage<DoubleValue>
    {
        internal override DoubleValue CalculateStatistics(DoubleValue value) => StatisticsCalculation.CalculateEma<DoubleValue, double>(value, LastValue);
    }
}
