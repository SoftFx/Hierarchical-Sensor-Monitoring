using HSMServer.Core.Model.Storages;

namespace HSMServer.Core.Model
{
    public sealed class EnumValuesStorage : ValuesStorage<EnumValue>
    {
        internal override EnumValue CalculateStatistics(EnumValue value) => StatisticsCalculation.CalculateEma<EnumValue, int>(LastValue, value);

        internal override EnumValue RecalculateStatistics(EnumValue value) => StatisticsCalculation.RecalculateEma<EnumValue, int>(LastValue, value);
    }
}