using HSMDataCollector.Core;
using System.Collections.Concurrent;

namespace HSMServer.BackgroundServices
{
    internal sealed class ClientStatisticsSensors
    {
        public const string TotalGroup = "_Total";

        private readonly ConcurrentDictionary<string, WebRequestNode> _selfSensors = new();
        private readonly IDataCollector _collector;


        // The factory overload: the node (four sensors) is built only when the client is new,
        // not on every request and every session frame that looks it up.
        public WebRequestNode this[string id] => id is null ? null : _selfSensors.GetOrAdd(id, key => new WebRequestNode(_collector, key));

        public TotalWebRequestNode Total { get; }


        internal ClientStatisticsSensors(IDataCollector collector)
        {
            _collector = collector;

            Total = new TotalWebRequestNode(collector);
        }
    }
}