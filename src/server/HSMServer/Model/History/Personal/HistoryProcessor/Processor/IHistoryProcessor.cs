using HSMServer.Core.Model;
using System.Collections.Generic;

namespace HSMServer.Model.History
{
    public interface IHistoryProcessor
    {
        List<BaseValue> ProcessingAndCompression(List<BaseValue> values, int compressedValuesCount);

        string GetCsvHistory(List<BaseValue> originalData);
    }
}