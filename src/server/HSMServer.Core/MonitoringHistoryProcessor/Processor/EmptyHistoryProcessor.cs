using HSMServer.Core.Model;
using System.Collections.Generic;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    internal sealed class EmptyHistoryProcessor : HistoryProcessorBase
    {
        public override string GetCsvHistory(List<BaseValue> originalData) => string.Empty;
    }
}
