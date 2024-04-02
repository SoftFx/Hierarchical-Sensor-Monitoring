using HSMDataCollector.Core;
using HSMServer.ServerConfiguration;
using HSMServer.WebRequestsNodes;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace HSMServer.BackgroundServices
{
    internal sealed class ClientStatistics
    {
        public const string TotalGroup = "_Total";

        private readonly ConcurrentDictionary<string, WebRequestNode> _selfSensors = new();
        private readonly IDataCollector _collector;
        private readonly IOptionsMonitor<MonitoringOptions> _optionsMonitor;


        public WebRequestNode this[string id] => id is null ? null : _selfSensors.GetOrAdd(id, new WebRequestNode(_collector, id));

        public TotalWebRequestNode Total { get; }


        internal ClientStatistics(IDataCollector collector, IOptionsMonitor<MonitoringOptions> optionsMonitor)
        {
            _collector = collector;
            _optionsMonitor = optionsMonitor;

            Total = new TotalWebRequestNode(collector);
        }
    }
}