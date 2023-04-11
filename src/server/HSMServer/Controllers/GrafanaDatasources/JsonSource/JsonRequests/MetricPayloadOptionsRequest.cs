namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public class MetricPayloadOptionsRequest
    {
        public string Metric { get; set; }

        public string Name { get; set; }

        public Payload Payload { get; set; } = new();
    }
}