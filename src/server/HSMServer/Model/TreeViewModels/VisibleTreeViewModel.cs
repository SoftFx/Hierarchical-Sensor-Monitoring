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
            var node = FilterNodes(product); // full tree build O(n) n - count nodes

            void ReduceNesting(NodeShallowModel node, int depth)
            {
                depth--;
                foreach (var subNode in node.Nodes)
                {
                    if (depth <= 0)
                    {
                        if (!NodesToRender.TryGetValue(subNode.Data.Id, out _))
                        {
                            subNode.Sensors.Clear();
                            subNode.Nodes.Clear();
                        }
                    }

                    ReduceNesting(subNode, depth); // without checking depth - O(n)
                }
            }

            ReduceNesting(node, product.RootProduct.DefinedRenderDepth);

            // total to current moment is O(n + n*m)

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

    private NodeShallowModel FilterNodes(ProductNodeViewModel product)
    {
        var node = new NodeShallowModel(product, _currentUser);

        foreach (var (_, childNode) in product.Nodes)
            node.AddChild(FilterNodes(childNode), _currentUser);

        foreach (var (_, sensor) in product.Sensors)
            node.AddChild(new SensorShallowModel(sensor, _currentUser), _currentUser);

        return node;
    }

    public BaseShallowModel GetUserNode(ProductNodeViewModel node)
    {
        var test = FilterNodes(node);

        if (test.VisibleSensorsCount > 0 || _currentUser.IsEmptyProductVisible(node))
        {
            foreach (var nestedNode in test.Nodes)
            {
                nestedNode.Sensors.Clear();
                nestedNode.Nodes.Clear();
            }

            return test;
        }

        return default;
    }
}