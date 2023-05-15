using System.Collections.Generic;

namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public class QueryHistoryRequest
    {
        public List<TargetMetric> Targets { get; set; }

        public TimeRange Range { get; set; }

        public int IntervalMs { get; set; }

        public int MaxDataPoints { get; set; }
    }
}