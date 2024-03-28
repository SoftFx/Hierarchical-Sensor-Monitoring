using HSMDataCollector.Core;
using System.Collections.Concurrent;
using HSMServer.ServerConfiguration.Monitoring;
using HSMServer.WebRequestsNodes;
using Microsoft.Extensions.Options;

namespace HSMServer.BackgroundServices
{
    internal sealed class ClientStatistics
    {
        private const string TotalGroup = "_Total";
        
        private readonly ConcurrentDictionary<string, WebRequestNode> _selfSensors = new();
        private readonly IDataCollector _collector;
        private readonly IOptionsMonitor<MonitoringOptions> _optionsMonitor;
        

        public WebRequestNode this[string id] => _selfSensors.GetOrAdd(id, AddSensors);

        public TotalWebRequestNode Total => this[TotalGroup] as TotalWebRequestNode;


        internal ClientStatistics(IDataCollector collector, IOptionsMonitor<MonitoringOptions> optionsMonitor)
        {
            _collector = collector;
            _optionsMonitor = optionsMonitor;
        }

        
        private WebRequestNode AddSensors(string path = null)
        {
            var id = path ?? TotalGroup;

            return id is TotalGroup ? new TotalWebRequestNode(_collector, id) : new WebRequestNode(_collector, id);
        }
    }
}