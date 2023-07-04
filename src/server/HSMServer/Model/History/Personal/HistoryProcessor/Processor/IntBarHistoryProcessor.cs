using HSMServer.Core.Model;

namespace HSMServer.Model.History
{
    internal sealed class IntBarHistoryProcessor : BarHistoryProcessor<int>
    {
        protected override int DefaultMax { get; } = int.MinValue;

        protected override int DefaultMin { get; } = int.MaxValue;


        protected override IntegerBarValue GetBarValue(SummaryBarItem<int> summary) =>
            new()
            {
                Count = summary.Count,
                OpenTime = summary.OpenTime.ToUniversalTime(),
                CloseTime = summary.CloseTime.ToUniversalTime(),
                Min = summary.Min,
                Max = summary.Max,
                Mean = summary.Mean,
                Percentiles = summary.Percentiles,
                Time = summary.CloseTime.ToUniversalTime(),
                ReceivingTime = summary.CloseTime.ToUniversalTime(),
            };

        protected override int Average(int value1, int value2) =>
            (value1 + value2) / 2;

        protected override int Convert(decimal value) => (int)value;

        protected override decimal GetComposition(int value1, int value2) =>
            value1 * value2;
    }
}