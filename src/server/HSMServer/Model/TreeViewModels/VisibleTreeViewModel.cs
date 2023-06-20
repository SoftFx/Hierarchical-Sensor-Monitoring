using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.UserTreeShallowCopy;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.TreeViewModels;

public sealed class VisibleTreeViewModel
{
    public const int RenderWidth = 100;
    
    
    private readonly User _user;

    public HashSet<Guid> OpenedNodes { get; } = new();


    public event Func<List<FolderModel>> GetFolders;
    public event Func<User, List<ProductNodeViewModel>> GetUserProducts;


    public VisibleTreeViewModel(User user)
    {
        _user = user;
    }


    public void AddRenderingNode(Guid id)
    {
        lock (_user)
        {
            OpenedNodes.Add(id);
        }
    }

    public void RemoveRenderingNode(Guid id)
    {
        lock (_user)
        {
            OpenedNodes.Remove(id);
        }
    }
    
    public List<BaseShallowModel> GetUserTree()
    {
        var products = GetUserProducts?.Invoke(_user).GetOrdered(_user);
        var folders = GetFolders?.Invoke().GetOrdered(_user).ToDictionary(k => k.Id, v => new FolderShallowModel(v, _user));

        var tree = new List<BaseShallowModel>(1 << 4);
        var folderTree = new List<BaseShallowModel>(1 << 4);

        foreach (var product in products)
        {
            var node = FilterNodes(product);

            if (IsVisibleNode(node, product))
            {
                var folderId = node.Data.FolderId;

                if (folderId.HasValue && folders.TryGetValue(folderId.Value, out var folder))
                    folder.AddChild(node, _user);
                else
                    tree.Add(node);
            }
        }

        foreach (var folder in folders.Values)
        {
            var viewEmptyFolder = _user.IsFolderAvailable(folder.Data.Id) && _user.TreeFilter.ByVisibility.Empty.Value;

            if (!folder.IsEmpty || viewEmptyFolder)
                folderTree.Add(folder);
        }

        folderTree.AddRange(tree);

        return folderTree;
    }

    public NodeShallowModel GetUserNode(ProductNodeViewModel node)
    {
        var currentNode = FilterNodes(node);

        return IsVisibleNode(currentNode, node) ? currentNode : default;
    }

    private NodeShallowModel FilterNodes(ProductNodeViewModel product, int depth = 1)
    {
        var node = new NodeShallowModel(product, _user);
        var currentWidth = 0;

        var toRender = OpenedNodes.Contains(product.Id) || depth > 0;
        foreach (var childNode in product.Nodes.Values.GetOrdered(_user))
        {
            var filterNodes = FilterNodes(childNode, --depth);
            node.AddChildState(filterNodes, _user);

            if (toRender && IsVisibleNode(filterNodes, filterNodes.Data) && currentWidth++ <= RenderWidth)
                node.AddChild(filterNodes);
        }

        foreach (var sensor in product.Sensors.Values.GetOrdered(_user))
        {
            var shallowSensor = new SensorShallowModel(sensor, _user);

            node.AddChildState(shallowSensor, _user);

            if (toRender && _user.IsSensorVisible(shallowSensor.Data) && currentWidth++ <= RenderWidth)
                node.AddChild(shallowSensor);
        }

        return node;
    }

    private bool IsVisibleNode(NodeShallowModel node, ProductNodeViewModel product) => node.VisibleSensorsCount > 0 || _user.IsEmptyProductVisible(product);
}