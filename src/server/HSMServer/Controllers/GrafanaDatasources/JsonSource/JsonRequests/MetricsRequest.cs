namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public class MetricsRequest
    {
        public string Metric { get; set; }

        public Payload Payload { get; set; } = new();
    }
}