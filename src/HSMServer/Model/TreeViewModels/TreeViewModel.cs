using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Helpers;
using HSMServer.Core.Model.Authentication;
using HSMServer.Model.AccessKeysViewModels;
using System;
using System.Collections.Concurrent;

namespace HSMServer.Model.TreeViewModels
{
    public class TreeViewModel
    {
        private readonly ITreeValuesCache _treeValuesCache;
        private readonly IUserManager _userManager;


        public ConcurrentDictionary<string, ProductNodeViewModel> Nodes { get; } = new();

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
                AddNewProductViewModel(product);

            foreach (var product in products)
                foreach (var (_, subProduct) in product.SubProducts)
                    Nodes[product.Id].AddSubNode(Nodes[subProduct.Id]);
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

        private void ChangeSensorHandler(SensorModel model, TransactionType transaction)
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

        private void AddNewSensorViewModel(SensorModel sensor, ProductNodeViewModel parent)
        {
            var viewModel = new SensorNodeViewModel(sensor);

            parent.AddSensor(viewModel);
            Sensors.TryAdd(viewModel.Id, viewModel);
        }

        private void AddNewAccessKeyViewModel(AccessKeyModel key, ProductNodeViewModel parent)
        {
            var viewModel = new AccessKeyViewModel(key, parent.Name, GetAccessKeyAuthorName(key));

            parent.AddAccessKey(viewModel);
            AccessKeys.TryAdd(key.Id, viewModel);
        }

        private string GetAccessKeyAuthorName(AccessKeyModel key) =>
            Guid.TryParse(key.AuthorId, out var authorId)
                ? _userManager.GetUser(authorId)?.UserName
                : key.AuthorId;
    }
}
