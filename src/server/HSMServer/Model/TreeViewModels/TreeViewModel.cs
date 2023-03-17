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
    public sealed class TreeViewModel
    {
        private readonly IUserManager _userManager;
        private readonly ITreeValuesCache _cache;


        public ConcurrentDictionary<Guid, AccessKeyViewModel> AccessKeys { get; } = new();

        public ConcurrentDictionary<Guid, SensorNodeViewModel> Sensors { get; } = new();

        public ConcurrentDictionary<Guid, ProductNodeViewModel> Nodes { get; } = new();


        public TreeViewModel(ITreeValuesCache cache, IUserManager userManager)
        {
            _userManager = userManager;
            _cache = cache;

            _cache.ChangeProductEvent += ChangeProductHandler;
            _cache.ChangeSensorEvent += ChangeSensorHandler;
            _cache.ChangeAccessKeyEvent += ChangeAccessKeyHandler;

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
            var products = GetRootProductsWithRecalculation();

            if (user == null || user.IsAdmin)
                return products.ToList();

            if (user.ProductsRoles == null || user.ProductsRoles.Count == 0)
                return new List<ProductNodeViewModel>();

            return products.Where(p => user.IsProductAvailable(p.Id)).ToList();
        }

        internal void RecalculateNodesCharacteristics()
        {
            foreach (var node in GetRootProductsWithRecalculation())
                node.RecalculateCharacteristics();
        }

        internal IEnumerable<ProductNodeViewModel> GetRootProducts() => Nodes.Where(x => x.Value.Parent is null).Select(x => x.Value);

        private IEnumerable<ProductNodeViewModel> GetRootProductsWithRecalculation()
        {
            return GetRootProducts().Select(x => x.RecalculateCharacteristics());
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
            foreach (var product in _cache.GetProducts())
                AddNewProductViewModel(product);

            foreach (var (_, key) in AccessKeys) //??? TODO Remove UpdateNodePath
                key.UpdateNodePath();
        }

        private void ChangeProductHandler(ProductModel model, ActionType transaction)
        {
            switch (transaction)
            {
                case ActionType.Add:
                    AddNewProductViewModel(model);
                    break;

                case ActionType.Update:
                    if (Nodes.TryGetValue(model.Id, out var product))
                        product.Update(model);
                    break;

                case ActionType.Delete:
                    Nodes.TryRemove(model.Id, out _);

                    if (model.Parent != null && Nodes.TryGetValue(model.Parent.Id, out var parentProduct))
                        parentProduct.Nodes.TryRemove(model.Id, out var _);

                    break;
            }
        }

        private void ChangeSensorHandler(BaseSensorModel model, ActionType transaction)
        {
            switch (transaction)
            {
                case ActionType.Add:
                    if (Nodes.TryGetValue(model.Parent.Id, out var parent))
                        AddNewSensorViewModel(model, parent);

                    break;

                case ActionType.Update:
                    if (!Sensors.TryGetValue(model.Id, out var sensor))
                        return;

                    sensor.Update(model);
                    break;

                case ActionType.Delete:
                    Sensors.TryRemove(model.Id, out _);

                    if (Nodes.TryGetValue(model.Parent.Id, out var parentProduct))
                        parentProduct.Sensors.TryRemove(model.Id, out var _);

                    break;
            }
        }

        private void ChangeAccessKeyHandler(AccessKeyModel model, ActionType transaction)
        {
            switch (transaction)
            {
                case ActionType.Add:
                    if (Nodes.TryGetValue(model.ProductId, out var parent))
                        AddNewAccessKeyViewModel(model, parent);

                    break;

                case ActionType.Update:
                    if (!AccessKeys.TryGetValue(model.Id, out var accessKey))
                        return;

                    accessKey.Update(model);
                    break;

                case ActionType.Delete:
                    AccessKeys.TryRemove(model.Id, out _);

                    if (Nodes.TryGetValue(model.ProductId, out var parentProduct))
                        parentProduct.AccessKeys.TryRemove(model.Id, out var _);

                    break;
            }
        }

        private ProductNodeViewModel AddNewProductViewModel(ProductModel product)
        {
            var node = new ProductNodeViewModel(product);

            Nodes.TryAdd(node.Id, node);

            if (product.Parent != null && Nodes.TryGetValue(product.Parent.Id, out var parent))
                parent.AddSubNode(node);

            foreach (var (_, child) in product.SubProducts)
                AddNewProductViewModel(child);

            foreach (var (_, sensor) in product.Sensors)
                AddNewSensorViewModel(sensor, node);

            foreach (var (_, key) in product.AccessKeys)
                AddNewAccessKeyViewModel(key, node);

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
                var user = _userManager[key.AuthorId.Value];
                if (user != null)
                    return user.Name;
            }

            return key.AuthorId?.ToString();
        }
    }
}