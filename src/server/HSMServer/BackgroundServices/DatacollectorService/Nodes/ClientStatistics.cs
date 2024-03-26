using HSMDataCollector.Core;
using HSMServer.ServerConfiguration.Monitoring;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using HSMServer.WebRequestsNodes;

namespace HSMServer.BackgroundServices
{
    internal sealed class ClientStatistics
    {
        private readonly ConcurrentDictionary<string, WebRequestNode> _selfSensors = new();
        private readonly IDataCollector _collector;


        private const string RecvBytes = "Recv Bytes";
        private const string SentBytes = "Sent Bytes";
        private const string RecvSensors = "Recv Sensors";
        private const string SentSensors = "Sent Sensors";
        private const string RequestPerSecond = "RPS";
        private const string TotalGroup = "_Total";

        public const string ClientNode = "Clients";

        public WebRequestNode this[string id] => _selfSensors.GetOrAdd(id, AddSensors);

        public TotalWebRequestNode Total => this[TotalGroup] as TotalWebRequestNode;


        internal ClientStatistics(IDataCollector collector)
        {
            _collector = collector;
        }

        
        private WebRequestNode AddSensors(string path = null)
        {
            var id = path ?? TotalGroup;
            if (id is TotalGroup)
            {
                return new TotalWebRequestNode()
                {
                    SentBytes = _collector.CreateM1RateSensor($"{ClientNode}/{id}/{SentBytes}",
                        "Number of bytes that were sent from server to client"),
                    ReceiveBytes = _collector.CreateM1RateSensor($"{ClientNode}/{id}/{RecvBytes}",
                        "Number of bytes that were received from client"),
                    SentSensors = _collector.CreateM1RateSensor($"{ClientNode}/{id}/{SentSensors}",
                        "Number of sensors that were sent from server to client"),
                    ReceiveSensors = _collector.CreateM1RateSensor($"{ClientNode}/{id}/{RecvSensors}",
                        "Number of sensors that were received from client"),
                    RPS = _collector.CreateM1RateSensor($"{ClientNode}/{id}/{RequestPerSecond}",
                        "Number of requests that were receive")
                };
            }
            
            return new WebRequestNode
            {
                SentBytes = _collector.CreateM1RateSensor($"{ClientNode}/{id}/{SentBytes}",
                    "Number of bytes that were sent from server to client"),
                ReceiveBytes = _collector.CreateM1RateSensor($"{ClientNode}/{id}/{RecvBytes}",
                    "Number of bytes that were received from client"),
                SentSensors = _collector.CreateM1RateSensor($"{ClientNode}/{id}/{SentSensors}",
                    "Number of sensors that were sent from server to client"),
                ReceiveSensors = _collector.CreateM1RateSensor($"{ClientNode}/{id}/{RecvSensors}",
                    "Number of sensors that were received from client"),
            };
        }
    }
}