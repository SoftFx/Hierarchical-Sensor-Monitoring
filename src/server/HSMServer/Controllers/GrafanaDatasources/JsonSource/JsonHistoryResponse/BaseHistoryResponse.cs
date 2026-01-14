using System.Collections.Generic;
using HSMCommon.Model;


namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public abstract class BaseHistoryResponse
    {
        public string Target { get; init; }


        public abstract BaseHistoryResponse FillRows(List<BaseValue> rawData);
    }
}