using HSMCommon.Model;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMServer.Folders;
using HSMServer.Model.AccessKeysViewModels;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
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


        public TreeViewModel(ITreeValuesCache cache, IFolderManager folderManager, IUserManager userManager)
        {
            _folderManager = folderManager;

            _userManager = userManager;
            _cache = cache;

            _cache.ChangeProductEvent += ChangeProductHandler;
            _cache.ChangeSensorEvent += ChangeSensorHandler;
            _cache.ChangeAccessKeyEvent += ChangeAccessKeyHandler;

            foreach (var user in _userManager.GetUsers())
                user.Tree.GetUserProducts += GetUserProducts;

            userManager.Added += AddUserHandler;
            userManager.Removed += RemoveUserHandler;

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

        /// <summary>
        /// Groups a node's chart-comparable descendant sensors by (type, effective unit) and returns
        /// the groups that contain at least two sensors, ordered from the largest group to the smallest.
        /// Drives the overlay endpoint; use <see cref="HasComparableChildGroup"/> for tab visibility.
        /// </summary>
        internal List<NodeSensorGroup> GetComparableChildGroups(Guid nodeId)
        {
            // Key on the raw enum codes (cheap struct equality) — no reflection in this per-sensor loop.
            // The display label is formatted once per surviving group.
            var groups = new Dictionary<(SensorType Type, int? Unit), List<Guid>>();

            foreach (var sensorId in GetAllNodeSensors(nodeId))
            {
                if (!Sensors.TryGetValue(sensorId, out var sensor) || !IsComparableChartSensor(sensor))
                    continue;

                var key = (sensor.Type, sensor.EffectiveUnitCode);

                if (!groups.TryGetValue(key, out var ids))
                    groups[key] = ids = new List<Guid>();

                ids.Add(sensorId);
            }

            var result = new List<NodeSensorGroup>();

            foreach (var (key, ids) in groups)
            {
                if (ids.Count < 2)
                    continue;

                var unitLabel = Sensors.TryGetValue(ids[0], out var first) ? first.EffectiveUnitLabel : string.Empty;
                result.Add(new NodeSensorGroup(key.Type, key.Unit, unitLabel, ids));
            }

            return result.OrderByDescending(group => group.SensorIds.Count)
                         .ThenBy(group => group.UnitLabel)
                         .ToList();
        }

        /// <summary>
        /// Cheap existence check for the node "Chart" tab: returns true as soon as any (type, unit) group
        /// reaches two sensors. Runs on every node selection, so it avoids the labels/sort/allocation of
        /// <see cref="GetComparableChildGroups"/>.
        /// </summary>
        internal bool HasComparableChildGroup(Guid nodeId)
        {
            var counts = new Dictionary<(SensorType Type, int? Unit), int>();

            foreach (var sensorId in GetAllNodeSensors(nodeId))
            {
                if (!Sensors.TryGetValue(sensorId, out var sensor) || !IsComparableChartSensor(sensor))
                    continue;

                var key = (sensor.Type, sensor.EffectiveUnitCode);

                if (counts.TryGetValue(key, out var count)) // already seen once -> this is the second
                    return true;

                counts[key] = count + 1;
            }

            return false;
        }

        // v1 overlays numeric line-able sensors only; enum/bool/version/timespan/string and service
        // status/alive step charts are out of scope here (tracked for later aggregation strategies).
        private static bool IsComparableChartSensor(SensorNodeViewModel sensor) =>
            !sensor.IsServiceStatus && !sensor.IsServiceAlive &&
            sensor.Type is SensorType.Integer or SensorType.Double or SensorType.Rate
                        or SensorType.IntegerBar or SensorType.DoubleBar;

        internal Guid GetBackgroundPlotId(SensorNodeViewModel sensor, bool isStatusService)
        {
            var sensorId = Guid.Empty;

            if (sensor is null)
                return sensorId;

            if (sensor.Path.Contains(".computer"))
                return sensorId;

            var name = isStatusService ? "Service status" : "Service alive";

            var splittedPath = sensor.FullPath.Split('/');
            var nodeIds = GetAllNodeSensors(sensor.RootProduct.Id);

            var pathComparisonValue = int.MinValue;
            var pathLength = int.MaxValue;

            foreach (var id in nodeIds)
                if (Sensors.TryGetValue(id, out var foundSensor))
                    if (CompareFunc(foundSensor))
                    {
                        var comparedPath = foundSensor.FullPath.Split('/');
                        var i = 0;
                        while (i < comparedPath.Length && i < splittedPath.Length && comparedPath[i] == splittedPath[i])
                            i++;

                        if (i > pathComparisonValue || (i == pathComparisonValue && pathLength > comparedPath.Length))
                        {
                            sensorId = foundSensor.Id;
                            pathComparisonValue = i;
                            pathLength = comparedPath.Length;
                        }
                    }

            return sensorId;

            bool CompareFunc(SensorNodeViewModel sensor) => sensor.Path.EndsWith($".module/Module Info/{name}") || sensor.Path.EndsWith($".module/{name}");
        }


        private ProductNodeViewModel AddNewProductViewModel(ProductModel product)
        {
            TryGetParentProduct(product, out var parent);
            TryGetParentFolder(product, out var folder);

            var node = new ProductNodeViewModel(product, parent, folder);

            node.Update(product);

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

            parent.AddSensor(viewModel); // add parent should be before update sensor settings
            viewModel.Update(sensor);

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
                    if (Nodes.TryRemove(model.Id, out _))
                        if (!model.IsRoot && Nodes.TryGetValue(model.Parent.Id, out var parentProduct))
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

        private void AddUserHandler(User user) => user.Tree.GetUserProducts += GetUserProducts;

        private void RemoveUserHandler(User user, InitiatorInfo _) => user.Tree.GetUserProducts -= GetUserProducts;

        private bool TryGetParentProduct(ProductModel product, out ProductNodeViewModel parent)
        {
            parent = default;

            return !product.IsRoot && Nodes.TryGetValue(product.Parent.Id, out parent);
        }

        private bool TryGetParentFolder(ProductModel product, out FolderModel parent) =>
            _folderManager.TryGetValueById(product.FolderId, out parent);
    }
}