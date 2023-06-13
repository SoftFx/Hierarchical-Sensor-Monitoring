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


    public static string GetDisabledJSTree() => 
        $$"""
        {
            "title": "disabled",
            "icon": "disabled",
            "time": "disabled",
            "isManager": "disabled",
            "isGrafanaEnabled": "disabled",
            "isAccountsEnable": "disabled",
            "groups": "disabled",
            "isMutedState": "disabled",
            "disabled": {{true.ToString().ToLower()}}
        }
        """;
    

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
        var folders = GetFolders?.Invoke().ToDictionary(k => k.Id, v => new FolderShallowModel(v, _user));

        var tree = new List<BaseShallowModel>(1 << 4);

        foreach (var product in GetUserProducts?.Invoke(_user))
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
                tree.Add(folder);
        }

        return tree;
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
        foreach (var (_, childNode) in product.Nodes)
        {
            var filterNodes = FilterNodes(childNode, --depth);
            node.AddChildState(filterNodes, _user);

            if (toRender && IsVisibleNode(filterNodes, filterNodes.Data) && currentWidth <= RenderWidth)
            {
                node.AddChild(filterNodes);
                currentWidth++;
            }
        }

        foreach (var (_, sensor) in product.Sensors)
        {
            var shallowSensor = new SensorShallowModel(sensor, _user);

            node.AddChildState(shallowSensor, _user);

            if (toRender && _user.IsSensorVisible(shallowSensor.Data) && currentWidth <= RenderWidth)
            {
                node.AddChild(shallowSensor);
                currentWidth++;
            }
        }

        return node;
    }

    private bool IsVisibleNode(NodeShallowModel node, ProductNodeViewModel product) => node.VisibleSensorsCount > 0 || _user.IsEmptyProductVisible(product);
}