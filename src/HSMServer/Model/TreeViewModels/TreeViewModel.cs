using HSMServer.Core.TreeValuesCache;
using System.Collections.Concurrent;

namespace HSMServer.Model.TreeViewModels
{
    public class TreeViewModel
    {
        private readonly ITreeValuesCache _treeValuesCache;

        public ConcurrentDictionary<string, ProductViewModel> Nodes { get; }
        public ConcurrentDictionary<string, SensorViewModel> Sensors { get; }


        public TreeViewModel(ITreeValuesCache valuesCache)
        {
            _treeValuesCache = valuesCache;

            Nodes = new ConcurrentDictionary<string, ProductViewModel>();
            Sensors = new ConcurrentDictionary<string, SensorViewModel>();

            BuildTree();
        }


        private void BuildTree()
        {
            var products = _treeValuesCache.GetTree();

            foreach (var product in products)
            {
                var node = new ProductViewModel(product);
                Nodes.TryAdd(node.Id, node);
            }

            foreach (var product in products)
                foreach (var (_, subProduct) in product.SubProducts)
                    Nodes[product.Id.ToString()].AddSubNode(Nodes[subProduct.Id.ToString()]);

            foreach (var (_, node) in Nodes)
                foreach (var sensor in node.Sensors)
                    Sensors.TryAdd(sensor.Key, sensor.Value);

            UpdateNodesCharacteristics();
        }

        private void UpdateNodesCharacteristics()
        {
            foreach (var (_, node) in Nodes)
                if (node.Parent == null)
                    node.Recursion();
        }
    }
}
