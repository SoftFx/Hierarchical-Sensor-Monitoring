using HSMServer.Core.Model;
using System.Collections.Generic;
using System.Text;

namespace HSMServer.Model.History
{
    internal sealed class StringHistoryProcessor : HistoryProcessorBase
    {
        public override string GetCsvHistory(List<BaseValue> values)
        {
            var sb = new StringBuilder(values.Count);

            sb.AppendLine($"Index,Time,Value,Comment");
            for (int i = 0; i < values.Count; ++i)
            {
                if (values[i] is StringValue value)
                    sb.AppendLine($"{i},{value.Time.ToUniversalTime():s},{value.Value},{value.Comment}");
            }

            return sb.ToString();
        }
    }
}
