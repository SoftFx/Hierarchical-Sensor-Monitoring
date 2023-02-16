using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Model.AccessKeysViewModels;
using HSMServer.Model.Authentication;
using HSMServer.Model.UserTreeShallowCopy;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.TreeViewModel
{
    public class TreeViewModel
    {
        private readonly ITreeValuesCache _treeValuesCache;
        private readonly IUserManager _userManager;


        public ConcurrentDictionary<Guid, ProductNodeViewModel> Nodes { get; } = new();

        public ConcurrentDictionary<Guid, SensorNodeViewModel> Sensors { get; } = new();

        public ConcurrentDictionary<Guid, AccessKeyViewModel> AccessKeys { get; } = new();


        public TreeViewModel(ITreeValuesCache valuesCache, IUserManager userManager)
        {
            _treeValuesCache = valuesCache;
            _treeValuesCache.ChangeProductEvent += ChangeProductHandler;
            _treeValuesCache.ChangeSensorEvent += ChangeSensorHandler;
            _treeValuesCache.ChangeAccessKeyEvent += ChangeAccessKeyHandler;

            _userManager = userManager;

            BuildTree();
        }


        public List<NodeShallowModel> GetUserTree(User user)
        {
            NodeShallowModel FilterNodes(ProductNodeViewModel product)
            {
                var node = new NodeShallowModel(product, user);

                foreach (var (_, childNode) in product.Nodes)
                    node.AddChild(FilterNodes(childNode), user);

                foreach (var (_, sensor) in product.Sensors)
                    node.AddChild(new SensorShallowModel(sensor, user), user);

                return node;
            }


            var tree = new List<NodeShallowModel>(1 << 4);

            foreach (var (_, product) in Nodes)
                if (product.Parent == null && user.IsProductAvailable(product.Id))
                {
                    var node = FilterNodes(product);
                    if (node.VisibleSensorsCount > 0 || user.IsEmptyProductVisible(product))
                        tree.Add(node);
                }

            return tree;
        }

        public List<ProductNodeViewModel> GetUserProducts(User user)
        {
            var products = GetRootProducts();

            if (user == null || user.IsAdmin)
                return products.ToList();

            if (user.ProductsRoles == null || user.ProductsRoles.Count == 0)
                return new List<ProductNodeViewModel>();

            return products.Where(p => user.IsManager(p.Id)).ToList();
        }

        internal void RecalculateNodesCharacteristics()
        {
            foreach (var node in GetRootProducts())
                node.RecalculateCharacteristics();
        }

        private IEnumerable<ProductNodeViewModel> GetRootProducts()
        {
            return Nodes.Where(x => x.Value.Parent is null).Select(x => x.Value.RecalculateCharacteristics());
        }

        internal List<Guid> GetNodeAllSensors(Guid selectedNode)
        {
            var sensors = new List<Guid>(1 << 3);

            if (Sensors.TryGetValue(selectedNode, out var sensor))
                sensors.Add(sensor.Id);
            else if (Nodes.TryGetValue(selectedNode, out var node))
            {
                void GetNodeSensors(Guid nodeId)
                {
                    if (!Nodes.TryGetValue(nodeId, out var node))
                        return;

                    foreach (var (subNodeId, _) in node.Nodes)
                        GetNodeSensors(subNodeId);

                    foreach (var (sensorId, _) in node.Sensors)
                        sensors.Add(sensorId);
                }

                GetNodeSensors(node.Id);
            }

            return sensors;
        }

        private void BuildTree()
        {
            var products = _treeValuesCache.GetTree();

            foreach (var product in products)
                AddNewProductViewModel(product);

            foreach (var product in products)
                foreach (var (_, subProduct) in product.SubProducts)
                    Nodes[product.Id].AddSubNode(Nodes[subProduct.Id]);

            foreach (var (_, key) in AccessKeys)
                key.UpdateNodePath();
        }

        private void ChangeProductHandler(ProductModel model, TransactionType transaction)
        {
            switch (transaction)
            {
                case TransactionType.Add:
                    var newProduct = AddNewProductViewModel(model);

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

                    if (model.ParentProduct != null && Nodes.TryGetValue(model.ParentProduct.Id, out var parentProduct))
                        parentProduct.Nodes.TryRemove(model.Id, out var _);

                    break;
            }
        }

        private void ChangeSensorHandler(BaseSensorModel model, TransactionType transaction)
        {
            switch (transaction)
            {
                case TransactionType.Add:
                    if (Nodes.TryGetValue(model.ParentProduct.Id, out var parent))
                        AddNewSensorViewModel(model, parent);

                    break;

                case TransactionType.Update:
                    if (!Sensors.TryGetValue(model.Id, out var sensor))
                        return;

                    sensor.Update(model);
                    break;

                case TransactionType.Delete:
                    Sensors.TryRemove(model.Id, out _);

                    if (Nodes.TryGetValue(model.ParentProduct.Id, out var parentProduct))
                        parentProduct.Sensors.TryRemove(model.Id, out var _);

                    break;
            }
        }

        private void ChangeAccessKeyHandler(AccessKeyModel model, TransactionType transaction)
        {
            switch (transaction)
            {
                case TransactionType.Add:
                    if (Nodes.TryGetValue(model.ProductId, out var parent))
                        AddNewAccessKeyViewModel(model, parent);

                    break;

                case TransactionType.Update:
                    if (!AccessKeys.TryGetValue(model.Id, out var accessKey))
                        return;

                    accessKey.Update(model);
                    break;

                case TransactionType.Delete:
                    AccessKeys.TryRemove(model.Id, out _);

                    if (Nodes.TryGetValue(model.ProductId, out var parentProduct))
                        parentProduct.AccessKeys.TryRemove(model.Id, out var _);

                    break;
            }
        }

        private ProductNodeViewModel AddNewProductViewModel(ProductModel product)
        {
            var node = new ProductNodeViewModel(product);

            foreach (var (_, sensor) in product.Sensors)
                AddNewSensorViewModel(sensor, node);

            foreach (var (_, key) in product.AccessKeys)
                AddNewAccessKeyViewModel(key, node);

            Nodes.TryAdd(node.Id, node);

            return node;
        }

        private void AddNewSensorViewModel(BaseSensorModel sensor, ProductNodeViewModel parent)
        {
            var viewModel = new SensorNodeViewModel(sensor);

            parent.AddSensor(viewModel);
            Sensors.TryAdd(viewModel.Id, viewModel);
        }

        private void AddNewAccessKeyViewModel(AccessKeyModel key, ProductNodeViewModel parent)
        {
            var viewModel = new AccessKeyViewModel(key, parent, GetAccessKeyAuthorName(key));

            parent.AddAccessKey(viewModel);
            AccessKeys.TryAdd(key.Id, viewModel);
        }

        private string GetAccessKeyAuthorName(AccessKeyModel key)
        {
            if (key.AuthorId.HasValue)
            {
                var user = _userManager.GetUser(key.AuthorId.Value);
                if (user != null)
                    return user.UserName;
            }

            return key.AuthorId?.ToString();
        }
    }
}