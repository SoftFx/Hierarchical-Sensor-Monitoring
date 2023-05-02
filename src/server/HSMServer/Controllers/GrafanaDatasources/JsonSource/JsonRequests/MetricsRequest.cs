namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public class MetricsRequest
    {
        public string Metric { get; set; }

        public SelectedPayload Payload { get; set; } = new();
    }
}