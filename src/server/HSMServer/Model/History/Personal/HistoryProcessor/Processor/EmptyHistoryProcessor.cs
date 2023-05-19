using HSMServer.Core.Model;
using System.Collections.Generic;

namespace HSMServer.Model.History
{
    internal sealed class EmptyHistoryProcessor : HistoryProcessorBase
    {
        public override string GetCsvHistory(List<BaseValue> originalData) => string.Empty;
    }
}
