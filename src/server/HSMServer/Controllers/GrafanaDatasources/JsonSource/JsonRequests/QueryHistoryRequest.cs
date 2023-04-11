using System.Collections.Generic;

namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public class QueryHistoryRequest
    {
        public List<TargetMetric> Targets { get; set; }


        public string PanelId { get; set; }

        public TimeRange Range { get; set; }

        public long IntervalMs { get; set; }

        public long MaxDataPoints { get; set; }
    }
}