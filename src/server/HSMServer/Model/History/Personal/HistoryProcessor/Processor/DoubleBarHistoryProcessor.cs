using HSMServer.Core.Model;
using System.Globalization;

namespace HSMServer.Model.History
{
    internal sealed class DoubleBarHistoryProcessor : BarHistoryProcessor<double>
    {
        private readonly NumberFormatInfo _format;


        protected override double DefaultMax { get; } = double.MinValue;

        protected override double DefaultMin { get; } = double.MaxValue;


        public DoubleBarHistoryProcessor()
        {
            _format = new NumberFormatInfo { NumberDecimalSeparator = "." };
        }

        protected override DoubleBarValue GetBarValue(SummaryBarItem<double> summary) =>
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

        protected override double Average(double value1, double value2) =>
            (value1 + value2) / 2;

        protected override double Convert(decimal value) => (double)value;

        protected override decimal GetComposition(double value1, int value2) =>
            (decimal)value1 * value2;
    }
}
