using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.TreeViewModel
{
    /// <summary>
    /// The sensor tree as one web user may address it by id.
    /// <para>
    /// <see cref="TreeViewModel"/> and the folder manager are process-wide singletons: an id resolved from
    /// them proves nothing about the caller. Controllers that take a sensor, node or folder id from the
    /// client hold this class INSTEAD of the tree, so every lookup answers "not found" for an entity the
    /// user cannot see and a new action has no unchecked lookup to reach for.
    /// </para>
    /// <para>
    /// The rule is the one the tree itself is built by (<see cref="TreeViewModel.GetUserProducts"/>): a
    /// sensor or node is visible when the user is an admin or holds any role on its ROOT product; a folder
    /// is visible when the user is an admin, holds a role on the folder, or sees one of its products.
    /// Whether the user may CHANGE what they see (Manager role) is a separate question this class does
    /// not answer.
    /// </para>
    /// </summary>
    public sealed class UserTreeAccess
    {
        private readonly TreeViewModel _tree;
        private readonly IFolderManager _folders;


        public UserTreeAccess(TreeViewModel tree, IFolderManager folders)
        {
            _tree = tree;
            _folders = folders;
        }


        public bool TryGetSensor(User user, Guid id, out SensorNodeViewModel sensor)
        {
            if (_tree.Sensors.TryGetValue(id, out sensor) && IsVisible(user, sensor))
                return true;

            sensor = null;
            return false;
        }

        public bool TryGetNode(User user, Guid id, out ProductNodeViewModel node)
        {
            if (_tree.Nodes.TryGetValue(id, out node) && IsVisible(user, node))
                return true;

            node = null;
            return false;
        }

        public bool TryGetFolder(User user, Guid id, out FolderModel folder)
        {
            if (_folders.TryGetValue(id, out folder) && IsVisible(user, folder))
                return true;

            folder = null;
            return false;
        }

        /// <summary>True when <paramref name="id"/> is a sensor, node or folder the user can see.</summary>
        public bool IsVisible(User user, Guid id) =>
            TryGetSensor(user, id, out _) || TryGetNode(user, id, out _) || TryGetFolder(user, id, out _);

        public bool IsVisible(User user, NodeViewModel node) =>
            user is not null && RootOf(node) is { } root && HasProduct(user, root.Id);

        public bool IsVisible(User user, FolderModel folder) =>
            user is not null && folder is not null &&
            (user.IsFolderAvailable(folder.Id) || folder.Products.Keys.Any(id => HasProduct(user, id)));

        /// <summary>
        /// The products of a folder the user can see. A folder role does not imply a role on every product
        /// in it (a per-product role removal narrows the folder role), so the folder's own list is not it.
        /// </summary>
        public Dictionary<Guid, ProductNodeViewModel> GetVisibleProducts(User user, FolderModel folder) =>
            folder.Products.Where(p => IsVisible(user, p.Value)).ToDictionary(p => p.Key, p => p.Value);

        /// <summary>
        /// Ids of the sensors under <paramref name="id"/> (a sensor itself, a node subtree, or the visible
        /// products of a folder). Empty for an unknown id and for one the user cannot see.
        /// </summary>
        public List<Guid> GetSensorIds(User user, Guid id)
        {
            if (TryGetSensor(user, id, out _) || TryGetNode(user, id, out _))
                return _tree.GetAllNodeSensors(id);

            var sensors = new List<Guid>();

            if (TryGetFolder(user, id, out var folder))
                foreach (var productId in GetVisibleProducts(user, folder).Keys)
                    sensors.AddRange(_tree.GetAllNodeSensors(productId));

            return sensors;
        }

        private static bool HasProduct(User user, Guid productId) =>
            user.IsAdmin || (user.ProductsRoles is not null && user.IsProductAvailable(productId));

        // NodeViewModel.RootProduct casts on its way up and throws for an entry that is not attached to a
        // product (yet, or any more); this walk answers null for it, so such an entry is simply not found.
        private static ProductNodeViewModel RootOf(NodeViewModel node)
        {
            for (var current = node; current is not null; current = current.Parent as NodeViewModel)
                if (current is ProductNodeViewModel product && product.Parent is null or FolderModel)
                    return product;

            return null;
        }

        // The methods below take an entity this class has already handed out, never a raw id.

        public bool HasComparableChildGroup(ProductNodeViewModel node) => _tree.HasComparableChildGroup(node.Id);

        public List<NodeSensorGroup> GetComparableChildGroups(ProductNodeViewModel node) => _tree.GetComparableChildGroups(node.Id);

        /// <summary>
        /// The "Service alive" / "Service status" sensor drawn behind <paramref name="sensor"/>'s chart. It
        /// is searched in the sensor's own root product, so it is visible whenever the sensor is; the lookup
        /// goes through the same check all the same.
        /// </summary>
        public bool TryGetBackgroundSensor(User user, SensorNodeViewModel sensor, bool isStatusService, out SensorNodeViewModel background)
        {
            background = null;

            var id = _tree.GetBackgroundPlotId(sensor, isStatusService);

            return id != Guid.Empty && TryGetSensor(user, id, out background);
        }
    }
}
