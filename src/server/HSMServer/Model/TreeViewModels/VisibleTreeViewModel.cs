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

public class RemoveNodesRequestModel
{
    public Guid[] NodeIds { get; set; }
    
    public bool IsSearch { get; set; }
}


public record SearchPattern(string SearchParameter = "", bool IsMatchWord = false, bool IsSearchRefresh = false)
{
    public bool IsSearch { get; set; } = !string.IsNullOrEmpty(SearchParameter);
    
    public bool IsNameFits(string name) => IsMatchWord ? name.Equals(SearchParameter) : name.Contains(SearchParameter, StringComparison.OrdinalIgnoreCase);
}


public sealed class VisibleTreeViewModel
{
    private readonly ConcurrentDictionary<Guid, NodeShallowModel> _allTree = new();
    private readonly CHash<Guid> _openedNodes = new();
    private readonly CHash<Guid> _searchOpenedNodes = new(1 << 6);
    private readonly CHash<Guid> _addedSearchNodes = new(1 << 2);

    private readonly User _user;

    private SearchPattern _searchPattern = new();

    
    public event Func<User, List<ProductNodeViewModel>> GetUserProducts;
    public event Func<List<FolderModel>> GetFolders;


    public CHash<Guid> OpenedNodes => _searchPattern.IsSearch ? _searchOpenedNodes : _openedNodes;

    internal SensorRenderedHash SearchedSensors { get; } = new();


    public VisibleTreeViewModel(User user)
    {
        _user = user;
    }


    public void AddOpenedNode(Guid id) => OpenedNodes.Add(id);

    public void AddOpenedNodes(IEnumerable<Guid> ids) => _openedNodes.AddRange(ids);

    public void RemoveOpenedNode(RemoveNodesRequestModel request)
    {
        _searchPattern.IsSearch = request.IsSearch;
        OpenedNodes.Remove(request.NodeIds);
    }

    public void ClearOpenedNodes()
    {
        OpenedNodes.Clear();
        _addedSearchNodes.Clear();
    }


    public List<BaseShallowModel> GetUserTree(SearchPattern pattern)
    {
        _searchPattern = pattern;
        
        // products should be updated before folders because folders should contain updated products
        var products = GetUserProducts?.Invoke(_user).GetOrdered(_user);
        var folders = GetFolders?.Invoke().GetOrdered(_user).ToDictionary(k => k.Id, v => new FolderShallowModel(v, _user));

        var folderTree = new List<BaseShallowModel>(1 << 4);
        var tree = new List<BaseShallowModel>(1 << 4);
        
        SearchedSensors.Clear();
        _allTree.Clear();

        if (!_searchPattern.IsSearchRefresh && _searchPattern.IsSearch)
            ClearOpenedNodes();
        
        if (!_searchPattern.IsSearch)
            _addedSearchNodes.Clear();

        foreach (var product in products)
        {
            var shouldAddNode = _searchPattern.IsSearch ? CanAddSearchNode(product, out var node) : CanAddNode(product, out node);

            if (shouldAddNode)
            {
                var folderId = node.Data.FolderId;

                if (folderId.HasValue && folders.TryGetValue(folderId.Value, out var folder))
                    folder.AddChild(node, _user);
                else
                    tree.Add(node);
            }
        }

        folderTree.AddRange(folders.Values.Where(GetFolderFilter()));
        folderTree.AddRange(tree);

        return folderTree;
    }

    public NodeShallowModel LoadNode(ProductNodeViewModel globalModel, bool isSearchRefresh = false)
    {
        var id = globalModel.Id;

        if (_allTree.TryGetValue(id, out var node) && IsVisibleNodeForUser(node))
        {
            if (isSearchRefresh)
                _addedSearchNodes.Add(id);

            node.LoadRenderingNodes();
            AddOpenedNode(id);
        }

        return node;
    }

    
    private Func<FolderShallowModel, bool> GetFolderFilter() => _searchPattern.IsSearch 
        ? folder =>
        {
            var isFolderEmpty = folder.IsEmpty;

            if (folder.Products.Count == 1)
            {
                if (isFolderEmpty && folder.Products[0].ContentIsEmpty)
                    isFolderEmpty = !_searchPattern.IsNameFits(folder.Products[0].Data.Name);
            }

            return !isFolderEmpty || (_searchPattern.IsNameFits(folder.Data.Name) && IsVisibleFolderForUser(folder.Id));
        }
        : folder => !folder.IsEmpty || IsVisibleFolderForUser(folder.Id);
    
    private NodeShallowModel FilterSearchNodes(ProductNodeViewModel product, out bool toRender)
    {
        var node = new NodeShallowModel(product, _user, IsVisibleNodeForUser, IsVisibleSensorForUser);

        toRender = false;
        _allTree.TryAdd(product.Id, node);

        foreach (var nodeModel in GetSubNodes(product))
        {
            var subNode = node.AddChild(FilterSearchNodes(nodeModel, out var currentNodeToRender));

            if (_searchPattern.IsNameFits(subNode.Data.Name) || currentNodeToRender || _addedSearchNodes.Contains(subNode.Id))
            {
                toRender = node.ToRenderNode(subNode.Id);
                AddOpenedNode(subNode.Id);
            }
        }

        foreach (var sensorModel in GetSubSensors(product))
        {
            var sensor = node.AddChild(new SensorShallowModel(sensorModel, _user), _user);

            if (_searchPattern.IsNameFits(sensor.Data.Name) || _addedSearchNodes.Contains(node.Id))
            {
                toRender = node.ToRenderNode(sensor.Id);

                if (toRender)
                    SearchedSensors.Add(sensor.Id);
            }
        }

        return node;
    }

    private NodeShallowModel FilterNodes(ProductNodeViewModel product, int depth = 1)
    {
        var node = new NodeShallowModel(product, _user, IsVisibleNodeForUser, IsVisibleSensorForUser);

        _allTree.TryAdd(product.Id, node);

        var toRender = OpenedNodes.Contains(product.Id) || depth > 0;

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

    private bool CanAddNode(ProductNodeViewModel product, out NodeShallowModel node)
    {
        node = FilterNodes(product);

        return IsVisibleNodeForUser(node);
    }

    private bool CanAddSearchNode(ProductNodeViewModel product, out NodeShallowModel node)
    {
        node = FilterSearchNodes(product, out var toRender);

        bool isVisible = IsVisibleNodeForUser(node) && toRender || _searchPattern.IsNameFits(node.Data.Name);

        if (isVisible)
            AddOpenedNode(node.Id);

        return isVisible;
    }

    private IOrderedEnumerable<SensorNodeViewModel> GetSubSensors(ProductNodeViewModel product) => product.Sensors.Values.GetOrdered(_user);

    private IOrderedEnumerable<ProductNodeViewModel> GetSubNodes(ProductNodeViewModel product) => product.Nodes.Values.GetOrdered(_user);


    private bool IsVisibleNodeForUser(NodeShallowModel node) => node.VisibleSubtreeSensorsCount > 0 || _user.IsEmptyProductVisible(node.Data);

    private bool IsVisibleFolderForUser(Guid folderId) => _user.IsFolderAvailable(folderId) && _user.TreeFilter.ByVisibility.Empty.Value;

    private bool IsVisibleSensorForUser(SensorShallowModel sensor) => _user.IsSensorVisible(sensor.Data);
}