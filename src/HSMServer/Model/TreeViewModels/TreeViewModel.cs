using HSMServer.Core.Cache;
using HSMServer.Core.Cache.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

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

            UpdateNodesCharacteristics(Nodes.Values.ToList());
        }

        private void UpdateNodesCharacteristics(List<ProductViewModel> nodes)
        {
            foreach (var node in nodes)
                if (node.Parent == null)
                    node.Recursion();
        }

        private void NewValueEventHandler(ProductModel productModel)
        {
            var allProducts = new List<ProductModel>();
            AddSubProducts(productModel, allProducts);

            var updatedProducts = new List<ProductViewModel>();
            foreach (var product in allProducts)
                updatedProducts.Add(UpdateProduct(product));

            foreach (var product in allProducts)
                foreach (var (_, subProduct) in product.SubProducts)
                    if (!Nodes[product.Id].Nodes.ContainsKey(subProduct.Id))
                        Nodes[product.Id].AddSubNode(Nodes[subProduct.Id]);

            UpdateNodesCharacteristics(updatedProducts);
        }

        private void AddSubProducts(ProductModel model, List<ProductModel> allProducts)
        {
            foreach (var (_, subProduct) in model.SubProducts)
                AddSubProducts(subProduct, allProducts);

            allProducts.Add(model);
        }

        private ProductViewModel UpdateProduct(ProductModel model)
        {
            if (!Nodes.TryGetValue(model.Id, out var productVM))
            {
                productVM = new ProductViewModel(model);
                Nodes.TryAdd(productVM.Id, productVM);
            }
            else
                productVM.Update(model);

            return productVM;
        }
    }
}
