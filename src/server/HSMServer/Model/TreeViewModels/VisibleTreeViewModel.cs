using HSMCommon.Collections;
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
    private readonly CHash<Guid> _openedNodes = new();

    private readonly User _user;

    public event Func<List<FolderModel>> GetFolders;
    public event Func<User, List<ProductNodeViewModel>> GetUserProducts;


    public VisibleTreeViewModel(User user)
    {
        _user = user;
    }


    public void AddOpenedNode(Guid id) => _openedNodes.Add(id);

    public void RemoveOpenedNode(params Guid[] ids) => _openedNodes.Remove(ids);

    public void ClearOpenedNodes() => _openedNodes.Clear();


    public List<BaseShallowModel> GetUserTree()
    {
        _allTree.Clear();

        // products should be updated before folders because folders should contain updated products
        var products = GetUserProducts?.Invoke(_user).GetOrdered(_user);
        var folders = GetFolders?.Invoke().GetOrdered(_user).ToDictionary(k => k.Id, v => new FolderShallowModel(v, _user));

        var folderTree = new List<BaseShallowModel>(1 << 4);
        var tree = new List<BaseShallowModel>(1 << 4);

        foreach (var product in products)
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
            if (!folder.IsEmpty || IsVisibleEmptyFolder(folder.Id))
                folderTree.Add(folder);

        folderTree.AddRange(tree);

        return folderTree;
    }

    public List<BaseShallowModel> GetUserTree(string searchParameter)
    {
        _allTree.Clear();
        ClearOpenedNodes();
        // products should be updated before folders because folders should contain updated products
        var products = GetUserProducts?.Invoke(_user).GetOrdered(_user);
        var folders = GetFolders?.Invoke().GetOrdered(_user).ToDictionary(k => k.Id, v => new FolderShallowModel(v, _user));

        var folderTree = new List<BaseShallowModel>(1 << 4);
        var tree = new List<BaseShallowModel>(1 << 4);

        foreach (var product in products)
        {
            var node = FilterNodes(product, searchParameter, out var toRender);

            if (IsVisibleNode(node) && toRender || node.Data.Name.Contains(searchParameter))
            {
                AddOpenedNode(node.Id);
                var folderId = node.Data.FolderId;

                if (folderId.HasValue && folders.TryGetValue(folderId.Value, out var folder))
                    folder.AddChild(node, _user);
                else
                    tree.Add(node);
            }
        }

        folderTree.AddRange(folders.Values.Where(x => (x.IsNameContainsPattern(searchParameter) || !x.IsEmpty) && IsVisibleEmptyFolder(x.Id)));
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

    private NodeShallowModel FilterNodes(ProductNodeViewModel product, string searchParameter, out bool toRender)
    {
        var node = new NodeShallowModel(product, _user, IsVisibleNode, IsVisibleSensor);

        toRender = false;
        _allTree.TryAdd(product.Id, node);

        foreach (var nodeModel in GetSubNodes(product))
        {
            var subNode = node.AddChild(FilterNodes(nodeModel, searchParameter, out var currentNodeToRender));

            if (subNode.IsNameContainsPattern(searchParameter) || currentNodeToRender)
            {
                toRender = true;
                AddOpenedNode(subNode.Id);
                node.ToRenderNode(subNode.Id);
            }
        }

        foreach (var sensorModel in GetSubSensors(product))
        {
            var sensor = node.AddChild(new SensorShallowModel(sensorModel, _user), _user);

            if (sensor.IsNameContainsPattern(searchParameter))
            {
                toRender = true;
                node.ToRenderNode(sensor.Id);
            }
        }

        return node;
    }

    private NodeShallowModel FilterNodes(ProductNodeViewModel product, int depth = 1)
    {
        var node = new NodeShallowModel(product, _user, IsVisibleNode, IsVisibleSensor);

        _allTree.TryAdd(product.Id, node);

        var toRender = _openedNodes.Contains(product.Id) || depth > 0;

        foreach (var nodeModel in GetSubNodes(product))
        {
            var subNode = node.AddChild(FilterNodes(nodeModel, --depth));

            if (toRender)
                node.ToRenderNode(subNode.Id);
        }

        foreach (var sensorModel in GetSubSensors(product))
        {
            var sensor = node.AddChild(new SensorShallowModel(sensorModel, _user), _user);

            if (toRender)
                node.ToRenderNode(sensor.Id);
        }

        return node;
    }


    private IOrderedEnumerable<SensorNodeViewModel> GetSubSensors(ProductNodeViewModel product) => product.Sensors.Values.GetOrdered(_user);

    private IOrderedEnumerable<ProductNodeViewModel> GetSubNodes(ProductNodeViewModel product) => product.Nodes.Values.GetOrdered(_user);


    private bool IsVisibleEmptyFolder(Guid folderId) => _user.IsFolderAvailable(folderId) && _user.TreeFilter.ByVisibility.Empty.Value;

    private bool IsVisibleNode(NodeShallowModel node) => node.VisibleSubtreeSensorsCount > 0 || _user.IsEmptyProductVisible(node.Data);

    private bool IsVisibleSensor(SensorShallowModel sensor) => _user.IsSensorVisible(sensor.Data);
}