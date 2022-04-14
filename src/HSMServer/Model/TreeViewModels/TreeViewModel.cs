using HSMServer.Core.TreeValuesCache;
using HSMServer.Core.TreeValuesCache.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HSMServer.Model.TreeViewModels
{
    public class TreeViewModel
    {
        private readonly ITreeValuesCache _treeValuesCache;


        public ConcurrentDictionary<Guid, ProductViewModel> Nodes { get; }

        public ConcurrentDictionary<Guid, SensorViewModel> Sensors { get; }


        public TreeViewModel(ITreeValuesCache valuesCache)
        {
            _treeValuesCache = valuesCache;
            _treeValuesCache.NewValueEvent += NewValueEventHandler;

            Nodes = new ConcurrentDictionary<Guid, ProductViewModel>();
            Sensors = new ConcurrentDictionary<Guid, SensorViewModel>();

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
                    Nodes[product.Id].AddSubNode(Nodes[subProduct.Id]);

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

        private void NewValueEventHandler(ProductModel productModel)
        {
            var allProducts = new List<ProductModel>();
            AddSubProducts(productModel, allProducts);

            foreach (var product in allProducts)
                UpdateProduct(product);
        }

        private void AddSubProducts(ProductModel model, List<ProductModel> allProducts)
        {
            foreach (var (_, subProduct) in model.SubProducts)
                AddSubProducts(subProduct, allProducts);

            allProducts.Add(model);
        }

        private void UpdateProduct(ProductModel model)
        {
            if (!Nodes.TryGetValue(model.Id, out var productVM))
            {
                productVM = new ProductViewModel(model);
                Nodes.TryAdd(productVM.Id, productVM);
            }
            else
                productVM.Update(model);
        }
    }
}
