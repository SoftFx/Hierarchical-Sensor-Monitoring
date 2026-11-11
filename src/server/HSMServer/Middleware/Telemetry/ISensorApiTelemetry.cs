using HSMServer.BackgroundServices;

namespace HSMServer.Middleware.Telemetry
{
    // The self-monitoring counters of the public Sensor API ("Clients/..." sensors of the
    // server's own product). An interface so the HTTP pipeline, the ingestion service and
    // the collector session share one way to count, and so tests can run the real
    // pipeline without the server's self-monitoring collector.
    //
    // Every method counts into the "_Total" node and, when `telemetryPath` is not null,
    // into the client's own node (product/key/client name).
    public interface ISensorApiTelemetry
    {
        // One request arrived: an HTTP request, or one request frame of a session.
        void AddRequest(string telemetryPath, long? bodyBytes);

        // One answer left: an HTTP response, or one frame the server wrote to a session.
        void AddResponse(string telemetryPath, long? bodyBytes);

        // Sensor values accepted for storing.
        void AddReceivedValues(string telemetryPath, int count);
    }


    internal sealed class SensorApiTelemetry(DataCollectorWrapper collector) : ISensorApiTelemetry
    {
        private readonly ClientStatisticsSensors _statistics = collector.WebRequestsSensors;


        public void AddRequest(string telemetryPath, long? bodyBytes)
        {
            _statistics.Total.AddRequestData(bodyBytes);
            _statistics[telemetryPath]?.AddRequestData(bodyBytes);
        }

        public void AddResponse(string telemetryPath, long? bodyBytes)
        {
            _statistics.Total.AddResponseResult(bodyBytes);
            _statistics[telemetryPath]?.AddResponseResult(bodyBytes);
        }

        public void AddReceivedValues(string telemetryPath, int count)
        {
            _statistics[telemetryPath]?.AddReceiveData(count);
            _statistics.Total.AddReceiveData(count);
        }
    }
}
