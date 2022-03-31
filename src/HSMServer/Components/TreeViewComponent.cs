using HSMServer.Core.TreeValuesCache;
using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;

namespace HSMServer.Components
{
    public class TreeViewComponent : ViewComponent
    {
        private readonly ITreeValuesCache _treeValuesCache;

        public ConcurrentDictionary<string, NodeViewModel> Nodes { get; }


        public TreeViewComponent(ITreeValuesCache valuesCache)
        {
            _treeValuesCache = valuesCache;

            Nodes = new ConcurrentDictionary<string, NodeViewModel>();
        }


        public IViewComponentResult Invoke()
        {
            BuildTree();

            return View("Tree", this);
        }

        private void BuildTree()
        {
            var products = _treeValuesCache.GetTree();

            foreach (var product in products)
            {
                var node = new NodeViewModel(product);
                Nodes.TryAdd(node.Path, node);
            }

            foreach (var product in products)
                foreach (var (_, subProduct) in product.SubProducts)
                    Nodes[product.Id.ToString()].AddSubNode(Nodes[subProduct.Id.ToString()]);

            foreach (var (_, node) in Nodes)
                if (node.Parent == null)
                    node.Recursion();
        }
    }
}
