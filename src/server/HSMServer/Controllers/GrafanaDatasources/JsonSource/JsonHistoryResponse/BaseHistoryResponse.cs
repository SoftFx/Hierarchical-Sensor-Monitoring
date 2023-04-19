using HSMServer.Core.Model;
using System.Collections.Generic;

namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public abstract class BaseHistoryResponse
    {
        public string Target { get; init; }


        public abstract BaseHistoryResponse FillRows(List<BaseValue> rawData);
    }
}