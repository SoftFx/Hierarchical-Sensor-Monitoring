namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public class TargetMetric
    {
        public string Target { get; set; }

        public string RefId { get; set; }

        public HistoryPayload Payload { get; set; }
    }
}