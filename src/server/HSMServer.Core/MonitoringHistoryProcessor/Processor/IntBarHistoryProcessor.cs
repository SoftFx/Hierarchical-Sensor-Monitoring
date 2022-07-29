using HSMServer.Core.Model;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    internal sealed class IntBarHistoryProcessor : BarHistoryProcessor<int>
    {
        protected override int DefaultMax { get; } = int.MinValue;

        protected override int DefaultMin { get; } = int.MaxValue;


        protected override IntegerBarValue GetBarValue(SummaryBarItem<int> summary) =>
            new()
            {
                Count = summary.Count,
                OpenTime = summary.OpenTime,
                CloseTime = summary.CloseTime,
                Min = summary.Min,
                Max = summary.Max,
                Mean = summary.Mean,
                Percentiles = summary.Percentiles,
                Time = summary.CloseTime,
            };

        protected override int Average(int value1, int value2) =>
            (value1 + value2) / 2;

        protected override int Convert(decimal value) => (int)value;

        protected override decimal GetComposition(int value1, int value2) =>
            value1 * value2;
    }
}