using HSMServer.Core.Model;
using HSMServer.Extensions;
using System;
using System.Collections.Generic;

namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public class HistoryDatapointsResponse : BaseHistoryResponse
    {
        public List<object[]> Datapoints { get; set; } = new();


        public HistoryDatapointsResponse() { }


        public override BaseHistoryResponse FillRows(List<BaseValue> rawData)
        {
            foreach (var raw in rawData)
                AddRow(raw.Time, raw.RawValue);

            return Datapoints.Count > 0 ? this : null;
        }

        private void AddRow(DateTime time, object value)
        {
            Datapoints.Add(new object[]
            {
                value,
                time.ToUnixMilliseconds()
            });
        }
    }
}