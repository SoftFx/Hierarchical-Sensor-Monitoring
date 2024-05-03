using HSMDataCollector.Core;
using System.Collections.Concurrent;

namespace HSMServer.BackgroundServices
{
    internal sealed class ClientStatisticsSensors
    {
        public const string TotalGroup = "_Total";

        private readonly ConcurrentDictionary<string, WebRequestNode> _selfSensors = new();
        private readonly IDataCollector _collector;


        public WebRequestNode this[string id] => id is null ? null : _selfSensors.GetOrAdd(id, new WebRequestNode(_collector, id));

        public TotalWebRequestNode Total { get; }


        internal ClientStatisticsSensors(IDataCollector collector)
        {
            _collector = collector;

            Total = new TotalWebRequestNode(collector);
        }
    }
}