using System;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Extensions;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.UserTreeShallowCopy;

namespace HSMServer.Model.TreeViewModels;

public class VisibleTreeViewModel
{
    private readonly User _currentUser;
    public Dictionary<Guid, bool> NodesToRender { get; set; } = new();


    public VisibleTreeViewModel(User user)
    {
        _currentUser = user;
    }
    
    
    public List<BaseShallowModel> GetUserTree(IFolderManager folderManager, TreeViewModel.TreeViewModel treeViewModel)
    {
        var folders = folderManager.GetUserFolders(_currentUser)
            .ToDictionary(k => k.Id, v => new FolderShallowModel(v, _currentUser));
        var tree = new List<BaseShallowModel>(1 << 4);

        foreach (var product in GetUserProducts(treeViewModel.GetRootProducts()))
        {
            var node = FilterNodes(product, product.DefinedRenderDepth);

            if (node.VisibleSensorsCount > 0 || _currentUser.IsEmptyProductVisible(product))
            {
                var folderId = node.Data.FolderId;

                if (folderId.HasValue)
                {
                    if (!folders.TryGetValue(folderId.Value, out var folder))
                    {
                        folder = new FolderShallowModel(folderManager[folderId], _currentUser);
                        folders.Add(folderId.Value, folder);
                    }

                    folder.AddChild(node, _currentUser);
                }
                else
                    tree.Add(node);
            }
        }

        var isUserNoDataFilterEnabled = _currentUser.TreeFilter.ByVisibility.Empty.Value;
        foreach (var folder in folders.Values)
            if (folder.Nodes.Count > 0 || isUserNoDataFilterEnabled)
                tree.Add(folder);

        return tree;
    }

    public List<ProductNodeViewModel> GetUserProducts(IEnumerable<ProductNodeViewModel> rootProducts)
    {
        var products = rootProducts.Select(x => x.RecalculateCharacteristics());

        if (_currentUser == null || _currentUser.IsAdmin)
            return products.ToList();

        if (_currentUser.ProductsRoles == null || _currentUser.ProductsRoles.Count == 0)
            return new List<ProductNodeViewModel>();

        return products.Where(p => _currentUser.IsProductAvailable(p.Id)).ToList();
    }

    private NodeShallowModel FilterNodes(ProductNodeViewModel product, int depth)
    {
        var node = new NodeShallowModel(product, _currentUser);

        var toRender = _currentUser.VisibleTreeViewModel.NodesToRender.TryGetValue(product.Id, out _) || depth > 0;
        foreach (var (_, childNode) in product.Nodes)
            node.AddChild(FilterNodes(childNode, --depth), _currentUser, toRender);

        foreach (var (_, sensor) in product.Sensors)
            node.AddChild(new SensorShallowModel(sensor, _currentUser), _currentUser, toRender);

        return node;
    }

    public BaseShallowModel GetUserNode(ProductNodeViewModel node)
    {
        var currentNode = FilterNodes(node, 1);

        if (currentNode.VisibleSensorsCount > 0 || _currentUser.IsEmptyProductVisible(node))
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
}