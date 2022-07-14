using HSMServer.Core.Model;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    internal sealed class DoubleHistoryProcessor : HistoryProcessorBase
    {
        private readonly NumberFormatInfo _format;


        public DoubleHistoryProcessor()
        {
            _format = new NumberFormatInfo { NumberDecimalSeparator = "." };
        }


        public override string GetCsvHistory(List<BaseValue> values)
        {
            var sb = new StringBuilder(values.Count);

            sb.AppendLine($"Index,Time,Value,Comment");
            for (int i = 0; i < values.Count; ++i)
            {
                if (values[i] is DoubleValue value)
                    sb.AppendLine($"{i},{value.Time.ToUniversalTime():s},{value.Value.ToString(_format)},{value.Comment}");
            }

            return sb.ToString();
        }
    }
}
