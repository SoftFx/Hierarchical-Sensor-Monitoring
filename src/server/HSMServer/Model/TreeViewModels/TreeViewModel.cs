using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Folders;
using HSMServer.Model.AccessKeysViewModels;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.UserTreeShallowCopy;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.TreeViewModel
{
    public sealed class TreeViewModel
    {
        private readonly IFolderManager _folderManager;
        private readonly IUserManager _userManager;
        private readonly ITreeValuesCache _cache;


        public ConcurrentDictionary<Guid, AccessKeyViewModel> AccessKeys { get; } = new();

        public ConcurrentDictionary<Guid, SensorNodeViewModel> Sensors { get; } = new();

        public ConcurrentDictionary<Guid, ProductNodeViewModel> Nodes { get; } = new();


        public TreeViewModel(ITreeValuesCache cache, IUserManager userManager, IFolderManager folderManager)
        {
            _folderManager = folderManager;
            _userManager = userManager;
            _cache = cache;

            _cache.ChangeProductEvent += ChangeProductHandler;
            _cache.ChangeSensorEvent += ChangeSensorHandler;
            _cache.ChangeAccessKeyEvent += ChangeAccessKeyHandler;

            foreach (var product in _cache.GetProducts())
                AddNewProductViewModel(product);
        }


        public List<ProductNodeViewModel> GetUserProducts(User user)
        {
            var products = GetRootProducts().Select(x => x.RecalculateCharacteristics());

            if (user == null || user.IsAdmin)
                return products.ToList();

            if (user.ProductsRoles == null || user.ProductsRoles.Count == 0)
                return new List<ProductNodeViewModel>();

            return products.Where(p => user.IsProductAvailable(p.Id)).ToList();
        }

        public List<BaseShallowModel> GetUserTree(User user)
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

            var folders = _folderManager.GetUserFolders(user).ToDictionary(k => k.Id, v => new FolderShallowModel(v, user));
            var tree = new List<BaseShallowModel>(1 << 4);

            foreach (var product in GetUserProducts(user))
            {
                var node = FilterNodes(product);

                if (node.VisibleSensorsCount > 0 || user.IsEmptyProductVisible(product))
                {
                    var folderId = node.Data.FolderId;

                    if (folderId.HasValue && folders.TryGetValue(folderId.Value, out var folder))
                        folder.AddChild(node, user);
                    else
                        tree.Add(node);
                }
            }

            var isUserNoDataFilterEnabled = user.TreeFilter.ByHistory.Empty.Value;
            foreach (var folder in folders.Values)
                if (folder.Nodes.Count > 0 || isUserNoDataFilterEnabled)
                    tree.Add(folder);

            return tree;
        }


        internal IEnumerable<ProductNodeViewModel> GetRootProducts() =>
            Nodes.Where(x => x.Value.Parent is null or FolderModel).Select(x => x.Value);

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

        internal void UpdateProductNotificationSettings(ProductNodeViewModel product)
        {
            var update = new ProductUpdate
            {
                Id = product.Id,
                NotificationSettings = product.Notifications.ToEntity(),
            };

            _cache.UpdateProduct(update);
        }


        private ProductNodeViewModel AddNewProductViewModel(ProductModel product)
        {
            TryGetParentProduct(product, out var parent);
            TryGetParentFolder(product, out var folder);

            var node = new ProductNodeViewModel(product, parent, folder);

            Nodes.TryAdd(node.Id, node);

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
            var author = key.AuthorId.HasValue ? (_userManager[key.AuthorId.Value]?.Name ?? key.AuthorId.ToString()) : key.AuthorId?.ToString();
            var viewModel = new AccessKeyViewModel(key, parent, author);

            parent.AddAccessKey(viewModel);
            AccessKeys.TryAdd(key.Id, viewModel);
        }


        private void ChangeProductHandler(ProductModel model, ActionType action)
        {
            switch (action)
            {
                case ActionType.Add:
                    AddNewProductViewModel(model);
                    break;

                case ActionType.Update:
                    if (Nodes.TryGetValue(model.Id, out var product))
                    {
                        product.Update(model);

                        if (product.FolderId != model.FolderId)
                            product.UpdateFolder(_folderManager[model.FolderId]);
                    }
                    break;

                case ActionType.Delete:
                    if (Nodes.TryRemove(model.Id, out _) && model.Parent != null && Nodes.TryGetValue(model.Parent.Id, out var parentProduct))
                        parentProduct.Nodes.TryRemove(model.Id, out var _);
                    break;
            }
        }

        private void ChangeSensorHandler(BaseSensorModel model, ActionType action)
        {
            switch (action)
            {
                case ActionType.Add:
                    if (Nodes.TryGetValue(model.Parent.Id, out var parent))
                        AddNewSensorViewModel(model, parent);
                    break;

                case ActionType.Update:
                    if (Sensors.TryGetValue(model.Id, out var sensor))
                        sensor.Update(model);
                    break;

                case ActionType.Delete:
                    if (Sensors.TryRemove(model.Id, out _) && Nodes.TryGetValue(model.Parent.Id, out var parentProduct))
                        parentProduct.Sensors.TryRemove(model.Id, out var _);
                    break;
            }
        }

        private void ChangeAccessKeyHandler(AccessKeyModel model, ActionType action)
        {
            switch (action)
            {
                case ActionType.Add:
                    if (Nodes.TryGetValue(model.ProductId, out var parent))
                        AddNewAccessKeyViewModel(model, parent);
                    break;

                case ActionType.Update:
                    if (AccessKeys.TryGetValue(model.Id, out var accessKey))
                        accessKey.Update(model);
                    break;

                case ActionType.Delete:
                    if (AccessKeys.TryRemove(model.Id, out _) && Nodes.TryGetValue(model.ProductId, out var parentProduct))
                        parentProduct.AccessKeys.TryRemove(model.Id, out var _);
                    break;
            }
        }

        private bool TryGetParentProduct(ProductModel product, out ProductNodeViewModel parent)
        {
            parent = default;

            return product.Parent != null && Nodes.TryGetValue(product.Parent.Id, out parent);
        }

        private bool TryGetParentFolder(ProductModel product, out FolderModel parent) =>
            _folderManager.TryGetValueById(product.FolderId, out parent);
    }
}