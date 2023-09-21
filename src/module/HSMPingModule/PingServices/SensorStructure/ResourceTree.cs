using HSMPingModule.Config;
using NLog;
using System.Collections.Concurrent;

namespace HSMPingModule.PingServices
{
    internal sealed class ResourceTree : IDisposable
    {
        private readonly ConcurrentDictionary<string, ResourceNode> _resources = new();

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly ServiceConfig _config;


        internal Dictionary<string, List<ResourceSensor>> CountrySet { get; private set; }


        internal ResourceTree(ServiceConfig config)
        {
            _config = config;
            _config.OnChanged += RebuildTree;

            RebuildTree();
        }


        public void Dispose()
        {
            _config.OnChanged -= RebuildTree;
        }

        private void RebuildTree()
        {
            _logger.Info("Start tree rebuilding...");
            _logger.Info("Canceling old requests...");

            foreach (var node in _resources)
                node.Value.Dispose();

            _logger.Info($"All requests canceled");

            _resources.Clear();
            CountrySet.Clear();

            foreach (var nodeSetting in _config.ResourceSettings.WebSites)
                _resources.TryAdd(nodeSetting.Key, new ResourceNode(nodeSetting));

            CountrySet = _resources.SelectMany(u => u.Value.Countries).GroupBy(u => u.Country).ToDictionary(u => u.Key, u => u.ToList());

            _logger.Info("Stop tree rebuild");
        }
    }
}