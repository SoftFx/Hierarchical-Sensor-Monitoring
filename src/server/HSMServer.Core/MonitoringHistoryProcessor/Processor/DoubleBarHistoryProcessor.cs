using HSMServer.Core.Model;
using System.Globalization;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    internal sealed class DoubleBarHistoryProcessor : BarHistoryProcessor<double>
    {
        private readonly NumberFormatInfo _format;


        public DoubleBarHistoryProcessor()
        {
            _format = new NumberFormatInfo { NumberDecimalSeparator = "." };
        }


        protected override string GetCsvRow(BarBaseValue<double> value) =>
            $"{value.OpenTime.ToUniversalTime():s},{value.CloseTime.ToUniversalTime():s},{value.Min.ToString(_format)}," +
            $"{value.Max.ToString(_format)},{value.Mean.ToString(_format)},{value.Count},{value.LastValue.ToString(_format)}";

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
            (decimal)(value1 * value2);
    }
}
