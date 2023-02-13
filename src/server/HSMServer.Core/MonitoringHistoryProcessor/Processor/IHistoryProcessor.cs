using HSMServer.Core.Model;
using System.Collections.Generic;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    public interface IHistoryProcessor
    {
        List<BaseValue> ProcessingAndCompression(List<BaseValue> values, int compressedValuesCount);

        string GetCsvHistory(List<BaseValue> originalData);
    }
}