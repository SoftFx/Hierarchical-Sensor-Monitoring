using HSMServer.Core.Cache;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Helpers;
using HSMServer.Core.Model.Authentication;
using System;
using System.Collections.Concurrent;

namespace HSMServer.Model.TreeViewModels
{
    public class TreeViewModel
    {
        private readonly ITreeValuesCache _treeValuesCache;


        public ConcurrentDictionary<string, ProductNodeViewModel> Nodes { get; }

        public ConcurrentDictionary<Guid, SensorNodeViewModel> Sensors { get; }


        public TreeViewModel(ITreeValuesCache valuesCache)
        {
            _treeValuesCache = valuesCache;
            _treeValuesCache.ChangeProductEvent += ChangeProductHandler;
            _treeValuesCache.ChangeSensorEvent += ChangeSensorHandler;
            _treeValuesCache.UploadSensorDataEvent += UploadSensorDataHandler;

            Nodes = new ConcurrentDictionary<string, ProductNodeViewModel>();
            Sensors = new ConcurrentDictionary<Guid, SensorNodeViewModel>();

            BuildTree();
        }


        internal void UpdateNodesCharacteristics(User user)
        {
            var userIsAdmin = UserRoleHelper.IsAllProductsTreeAllowed(user);
            foreach (var (nodeId, node) in Nodes)
                if (node.Parent == null)
                    node.IsAvailableForUser = userIsAdmin || ProductRoleHelper.IsAvailable(nodeId, user.ProductsRoles);

            foreach (var (_, node) in Nodes)
                if (node.Parent == null)
                    node.Recursion();
        }

        private void BuildTree()
        {
            var products = _treeValuesCache.GetTree();

            foreach (var product in products)
            {
                var node = new ProductNodeViewModel(product);
                Nodes.TryAdd(node.Id, node);
            }

            foreach (var product in products)
                foreach (var (_, subProduct) in product.SubProducts)
                    Nodes[product.Id].AddSubNode(Nodes[subProduct.Id]);

            foreach (var (_, node) in Nodes)
                foreach (var sensor in node.Sensors)
                    Sensors.TryAdd(sensor.Key, sensor.Value);
        }

        private void ChangeProductHandler(ProductModel model, TransactionType transaction)
        {
            switch (transaction)
            {
                case TransactionType.Add:
                    var newProduct = new ProductNodeViewModel(model);
                    Nodes.TryAdd(newProduct.Id, newProduct);

                    if (model.ParentProduct != null && Nodes.TryGetValue(model.ParentProduct.Id, out var parent))
                        parent.AddSubNode(newProduct);

                    break;

                case TransactionType.Update:
                    if (!Nodes.TryGetValue(model.Id, out var product))
                        return;

                    product.Update(model);

                    break;

                case TransactionType.Delete:
                    Nodes.TryRemove(model.Id, out _);

                    break;
            }
        }

        private void ChangeSensorHandler(SensorModel model, TransactionType transaction)
        {
            switch (transaction)
            {
                case TransactionType.Add:
                    if (!Nodes.TryGetValue(model.ParentProduct.Id, out var parent))
                        return;

                    var newSensor = new SensorNodeViewModel(model);
                    parent.AddSensor(newSensor);

                    Sensors.TryAdd(newSensor.Id, newSensor);

                    break;

                case TransactionType.Update:
                    if (!Sensors.TryGetValue(model.Id, out var sensor))
                        return;

                    sensor.Update(model);

                    break;

                case TransactionType.Delete:
                    Sensors.TryRemove(model.Id, out _);

                    break;
            }
        }

        private void UploadSensorDataHandler(SensorModel model)
        {
            if (Sensors.TryGetValue(model.Id, out var sensor))
                sensor.Update(model);
        }
    }
}
