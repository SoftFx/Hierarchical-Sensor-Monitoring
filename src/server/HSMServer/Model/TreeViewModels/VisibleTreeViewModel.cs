using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.UserTreeShallowCopy;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.TreeViewModels;

public sealed class VisibleTreeViewModel
{
    private readonly ConcurrentDictionary<Guid, NodeShallowModel> _allTree = new();
    private readonly HashSet<Guid> _openedNodes = new();

    private readonly User _user;

    public event Func<List<FolderModel>> GetFolders;
    public event Func<User, List<ProductNodeViewModel>> GetUserProducts;


    public VisibleTreeViewModel(User user)
    {
        _user = user;
    }


    public void AddOpenedNode(Guid id)
    {
        lock (_user)
        {
            _openedNodes.Add(id);
        }
    }

    public void RemoveOpenedNode(Guid id)
    {
        lock (_user)
        {
            _openedNodes.Remove(id);
        }
    }

    public List<BaseShallowModel> GetUserTree()
    {
        _allTree.Clear();

        var folders = GetFolders?.Invoke().GetOrdered(_user).ToDictionary(k => k.Id, v => new FolderShallowModel(v, _user));

        var folderTree = new List<BaseShallowModel>(1 << 4);
        var tree = new List<BaseShallowModel>(1 << 4);

        foreach (var product in GetUserProducts?.Invoke(_user).GetOrdered(_user))
        {
            var node = FilterNodes(product);

            if (IsVisibleNode(node))
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
            var viewEmptyFolder = _user.IsFolderAvailable(folder.Id) && _user.TreeFilter.ByVisibility.Empty.Value;

            if (!folder.IsEmpty || viewEmptyFolder)
                folderTree.Add(folder);
        }

        folderTree.AddRange(tree);

        return folderTree;
    }

    public NodeShallowModel LoadNode(ProductNodeViewModel globalModel)
    {
        var id = globalModel.Id;

        if (_allTree.TryGetValue(id, out var node) && IsVisibleNode(node))
        {
            node.LoadRenderingNodes();
            AddOpenedNode(id);
        }

        return node;
    }

    private NodeShallowModel FilterNodes(ProductNodeViewModel product, int depth = 1)
    {
        var node = new NodeShallowModel(product, _user, IsVisibleNode, IsVisibleSensor);

        _allTree.TryAdd(product.Id, node);

        var toRender = _openedNodes.Contains(product.Id) || depth > 0;

        foreach (var nodeModel in product.Nodes.Values.GetOrdered(_user))
        {
            var subNode = FilterNodes(nodeModel, --depth);

            node.AddChild(subNode);

            if (toRender)
                node.ToRenderNode(subNode.Id);
        }

        foreach (var sensorModel in product.Sensors.Values.GetOrdered(_user))
        {
            var sensor = new SensorShallowModel(sensorModel, _user);

            node.AddChild(sensor, _user);

            if (toRender)
                node.ToRenderNode(sensor.Id);
        }

        return node;
    }


    private bool IsVisibleNode(NodeShallowModel node) => node.VisibleSubtreeSensorsCount > 0 || _user.IsEmptyProductVisible(node.Data);

    private bool IsVisibleSensor(SensorShallowModel sensor) => _user.IsSensorVisible(sensor.Data);
}