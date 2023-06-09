﻿using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Folders;
using HSMServer.Model.AccessKeysViewModels;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Authentication;

namespace HSMServer.Model.TreeViewModel
{
    public sealed class TreeViewModel
    {
        private readonly IFolderManager _folderManager;
        private readonly ITreeValuesCache _cache;
        private readonly IUserManager _userManager;


        public ConcurrentDictionary<Guid, AccessKeyViewModel> AccessKeys { get; } = new();

        public ConcurrentDictionary<Guid, SensorNodeViewModel> Sensors { get; } = new();

        public ConcurrentDictionary<Guid, ProductNodeViewModel> Nodes { get; } = new();


        public TreeViewModel(ITreeValuesCache cache, IFolderManager folderManager, IUserManager userManager)
        {
            _folderManager = folderManager;
            _userManager = userManager;
            _cache = cache;

            _cache.ChangeProductEvent += ChangeProductHandler;
            _cache.ChangeSensorEvent += ChangeSensorHandler;
            _cache.ChangeAccessKeyEvent += ChangeAccessKeyHandler;

            foreach (var user in _userManager.GetUsers())
            {
                user.Tree.GetUserProducts += GetUserProducts;
                user.Tree.GetUserFolders += _folderManager.GetUserFolders;
            }
            
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


        internal IEnumerable<ProductNodeViewModel> GetRootProducts() =>
            Nodes.Where(x => x.Value.Parent is null or FolderModel).Select(x => x.Value);

        internal List<Guid> GetAllNodeSensors(Guid selectedNode)
        {
            var sensors = new List<Guid>(1 << 3);


            void GetNodeSensors(Guid nodeId)
            {
                if (!Nodes.TryGetValue(nodeId, out var node))
                    return;

                foreach (var (subNodeId, _) in node.Nodes)
                    GetNodeSensors(subNodeId);

                foreach (var (sensorId, _) in node.Sensors)
                    sensors.Add(sensorId);
            }


            if (Sensors.TryGetValue(selectedNode, out var sensor))
                sensors.Add(sensor.Id);
            else if (Nodes.TryGetValue(selectedNode, out var node))
                GetNodeSensors(node.Id);
            else if (_folderManager.TryGetValue(selectedNode, out var folder))
            {
                foreach (var productId in folder.Products.Keys)
                    GetNodeSensors(productId);
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
            if (node.Notifications.Migrated)
                UpdateProductNotificationSettings(node);

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
            var viewModel = new AccessKeyViewModel(key, parent, key.AuthorId);

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
                    {
                        AddNewSensorViewModel(model, parent);

                        var root = parent.RootProduct;
                        if (!root.Notifications.Telegram.Chats.IsEmpty)
                        {
                            root.Notifications.Enable(model.Id);
                            UpdateProductNotificationSettings(root);
                        }
                    }
                    break;

                case ActionType.Update:
                    if (Sensors.TryGetValue(model.Id, out var sensor))
                        sensor.Update(model);
                    break;

                case ActionType.Delete:
                    if (Sensors.TryRemove(model.Id, out var removedSensor) && Nodes.TryGetValue(model.Parent.Id, out var parentProduct))
                    {
                        parentProduct.Sensors.TryRemove(model.Id, out var _);

                        if (removedSensor.RootProduct.Notifications.RemoveSensor(model.Id))
                            UpdateProductNotificationSettings(removedSensor.RootProduct);
                    }
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