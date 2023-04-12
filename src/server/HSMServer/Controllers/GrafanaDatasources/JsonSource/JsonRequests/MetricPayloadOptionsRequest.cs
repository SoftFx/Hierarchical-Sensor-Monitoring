namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public class MetricPayloadOptionsRequest
    {
        public string Metric { get; set; }

        public string Name { get; set; }

        public SelectedPayload Payload { get; set; } = new();
    }
}