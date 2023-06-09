using System;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Extensions;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.UserTreeShallowCopy;

namespace HSMServer.Model.TreeViewModels;

public sealed class VisibleTreeViewModel
{
    public const int DefinedRenderDepth = 2;
    
    
    private readonly User _user;
    
    
    public HashSet<Guid> NodesToRender { get; } = new();

    
    public event Func<User, List<FolderModel>> GetUserFolders;
    
    public event Func<User, List<ProductNodeViewModel>> GetUserProducts;
    

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
                        //folder = new FolderShallowModel(folderManager[folderId], _user);
                        //folders.Add(folderId.Value, folder);
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
        {
            foreach (var nestedNode in currentNode.Nodes)
            {
                nestedNode.Sensors.Clear();
                nestedNode.Nodes.Clear();
            }

            return currentNode;
        }

        return default;
    }

    private NodeShallowModel FilterNodes(ProductNodeViewModel product, int depth)
    {
        var node = new NodeShallowModel(product, _user);

        var toRender = NodesToRender.TryGetValue(product.Id, out _) || depth > 0;
        foreach (var (_, childNode) in product.Nodes)
            node.AddChild(FilterNodes(childNode, --depth), _user, toRender);

        foreach (var (_, sensor) in product.Sensors)
            node.AddChild(new SensorShallowModel(sensor, _user), _user, toRender);

        return node;
    }

    private bool IsVisibleNode(NodeShallowModel node, ProductNodeViewModel product) => node.VisibleSensorsCount > 0 || _user.IsEmptyProductVisible(product);
}