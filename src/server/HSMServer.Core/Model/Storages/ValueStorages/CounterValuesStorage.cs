namespace HSMServer.Core.Model.Storages.ValueStorages
{
    internal class CounterValuesStorage : ValuesStorage<CounterValue>
    {
        internal override CounterValue CalculateStatistics(CounterValue value) => StatisticsCalculation.CalculateEma<CounterValue, double>(LastValue, value);

        internal override CounterValue RecalculateStatistics(CounterValue value) => StatisticsCalculation.RecalculateEma<CounterValue, double>(LastValue, value);
    }
}