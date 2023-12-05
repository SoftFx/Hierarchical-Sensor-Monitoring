using HSMServer.Core.Model.Storages;

namespace HSMServer.Core.Model
{
    public sealed class IntegerValuesStorage : ValuesStorage<IntegerValue>
    {
        internal override IntegerValue CalculateStatistics(IntegerValue value) => StatisticsCalculation.CalculateEma<IntegerValue, int>(value, LastValue);
    }
}
