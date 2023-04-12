using HSMServer.Core.Model;
using HSMServer.Extensions;
using System;
using System.Collections.Generic;

namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public class HistoryDatapointsResponse : BaseHistoryResponse
    {
        public List<string[]> Datapoints { get; set; } = new();


        public HistoryDatapointsResponse() { }


        public override BaseHistoryResponse FillRows(List<BaseValue> rawData)
        {
            foreach (var raw in rawData)
                AddRow(raw.Time, raw.ShortInfo);

            return Datapoints.Count > 0 ? this : null;
        }

        private void AddRow(DateTime time, string value)
        {
            Datapoints.Add(new string[]
            {
                value,
                $"{time.ToUnixMilliseconds()}"
            });
        }
    }
}