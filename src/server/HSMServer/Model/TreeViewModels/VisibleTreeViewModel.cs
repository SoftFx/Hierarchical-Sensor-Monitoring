using System;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.UserTreeShallowCopy;

namespace HSMServer.Model.TreeViewModels;

public sealed class VisibleTreeViewModel
{
    public const int DefinedRenderDepth = 1;
    
    
    private readonly User _user;
    
    
    public HashSet<Guid> OpenedNodes { get; } = new();

    
    public delegate bool OutTryGetModel<TGuid, TFolderModel>(TGuid guid, out TFolderModel model);
    
    public event Func<User, List<FolderModel>> GetUserFolders;
    
    public event Func<User, List<ProductNodeViewModel>> GetUserProducts;

    public event OutTryGetModel<Guid, FolderModel> GetFolder; 


    public VisibleTreeViewModel(User user)
    {
        _user = user;
    }
    
    
    public List<BaseShallowModel> GetUserTree()
    {
        var folders = GetUserFolders?.Invoke(_user)
            .ToDictionary(k => k.Id, v => new FolderShallowModel(v, _user));
        var tree = new List<BaseShallowModel>(1 << 4);

        foreach (var product in GetUserProducts?.Invoke(_user))
        {
            var node = FilterNodes(product, DefinedRenderDepth);

            if (node.VisibleSensorsCount > 0 || _user.IsEmptyProductVisible(product))
            {
                var folderId = node.Data.FolderId;

                if (folderId.HasValue)
                {
                    if (!folders.TryGetValue(folderId.Value, out var folder))
                    {
                        if (GetFolder?.Invoke(folderId.Value, out var model) is not null)
                        {
                            folder = new FolderShallowModel(model, _user);
                            folders.Add(folderId.Value, folder);
                        }
                    }

                    folder.AddChild(node, _user);
                }
                else
                    tree.Add(node);
            }
        }

        var isUserNoDataFilterEnabled = _user.TreeFilter.ByVisibility.Empty.Value;
        foreach (var folder in folders.Values)
            if (folder.Nodes.Count > 0 || isUserNoDataFilterEnabled)
                tree.Add(folder);

        return tree;
    }

    public BaseShallowModel GetUserNode(ProductNodeViewModel node)
    {
        var currentNode = FilterNodes(node, 1);

        if (IsVisibleNode(currentNode, node))
            return currentNode;
        
        return default;
    }

    private NodeShallowModel FilterNodes(ProductNodeViewModel product, int depth)
    {
        var node = new NodeShallowModel(product, _user);

        var toRender = OpenedNodes.TryGetValue(product.Id, out _) || depth > 0;
        foreach (var (_, childNode) in product.Nodes)
        {
            var filterNodes = FilterNodes(childNode, --depth);
            node.AddChildState(filterNodes, _user);

            if (toRender && IsVisibleNode(node, node.Data))
                node.AddChild(filterNodes);
        }

        foreach (var (_, sensor) in product.Sensors)
        {
            var shallowSensor = new SensorShallowModel(sensor, _user);
            
            node.AddChildState(shallowSensor, _user);
            
            if (toRender && _user.IsSensorVisible(shallowSensor.Data))
                node.AddChild(shallowSensor);
        }

        return node;
    }

    private bool IsVisibleNode(NodeShallowModel node, ProductNodeViewModel product) => node.VisibleSensorsCount > 0 || _user.IsEmptyProductVisible(product);
}