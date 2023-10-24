using HSMServer.Core.Model;

namespace HSMServer.Model.History
{
    internal sealed class IntBarHistoryProcessor : BarHistoryProcessor<int>
    {
        protected override int DefaultMax { get; } = int.MinValue;

        protected override int DefaultMin { get; } = int.MaxValue;


        protected override BarBaseValue<int> GetBarValue(SummaryBarItem<int> summary) =>
            new IntegerBarValue()
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
                LastValue = summary.LastValue / ValuesCount,
                FirstValue = summary.FirstValue / ValuesCount
            };

        protected override int Average(int value1, int value2) =>
            (value1 + value2) / 2;

        protected override int Convert(double value) => (int)value;

        protected override double GetComposition(int value1, int value2) =>
            value1 * value2;
    }
}