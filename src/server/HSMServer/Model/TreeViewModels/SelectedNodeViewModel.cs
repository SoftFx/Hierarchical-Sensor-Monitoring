using System;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Folders;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.ViewModel;

namespace HSMServer.Model.TreeViewModels;

public class SelectedNodeViewModel
{
    private TreeViewModel.TreeViewModel _treeViewModel;
    private IFolderManager _folderManager;
    private ITreeValuesCache _cache;
    
    public BaseNodeViewModel SelectedNode { get; private set; }
    
    
    public NodeChildrenViewModel NodeChildrenSensors { get; } = new();
        
    public NodeChildrenViewModel NodeChildrenNodes { get; } = new();
    
    
    public void ConnectNode(BaseNodeViewModel newNode, TreeViewModel.TreeViewModel treeViewModel, IFolderManager folderManager, ITreeValuesCache cache)
    {
        _treeViewModel = treeViewModel;
        _folderManager = folderManager;
        _cache = cache;
        
        if (SelectedNode?.Id == newNode.Id)
            return;

        SelectedNode = newNode;
        
        _cache.ChangeProductEvent -= ChangeProductHandler;
        Subscribe();
        ReloadVisibleItems(SelectedNode);
    }

    public NodeChildrenViewModel ReloadPage(string accordionId, int pageNumber, int pageSize)
    {
        switch (accordionId)
        {
            case "Nodes":
                NodeChildrenNodes.ChangePageNumber(pageNumber).ChangePageSize(pageSize);
                
                if (SelectedNode is ProductNodeViewModel productNodeViewModel)
                    return NodeChildrenNodes.InitializeItems(productNodeViewModel.Nodes.Values).TurnOnPagination();

                return NodeChildrenNodes.InitializeItems((SelectedNode as FolderModel)?.Products.Values).TurnOnPagination();
            case "Sensors":
                return NodeChildrenSensors.ChangePageNumber(pageNumber)
                                          .ChangePageSize(pageSize)
                                          .InitializeItems((SelectedNode as ProductNodeViewModel)?.Sensors.Values).TurnOnPagination();
            default:
                return NodeChildrenNodes.ChangePageNumber(pageNumber)
                                        .ChangePageSize(pageSize)
                                        .InitializeItems((SelectedNode as FolderModel)?.Products.Values).TurnOnPagination();;
        }
    }
    
    
    private void ReloadVisibleItems(BaseNodeViewModel node)
    {
        Reset();
        
        if (node is ProductNodeViewModel productNodeViewModel)
        {
            NodeChildrenNodes.InitializeItems(productNodeViewModel.Nodes.Values).TurnOnPagination();
            NodeChildrenSensors.InitializeItems(productNodeViewModel.Sensors.Values).TurnOnPagination();
        }
        else if (node is FolderModel folder)
            NodeChildrenNodes.InitializeItems(folder.Products.Values).TurnOnPagination();
    }

    private void Reset()
    {
        NodeChildrenNodes.Reset();
        NodeChildrenSensors.Reset();
    }

    private void Subscribe()
    {
        _cache.ChangeProductEvent += ChangeProductHandler;
    }
  
    private void ChangeProductHandler(ProductModel model, ActionType action)
    {
        if (_folderManager.TryGetValue(SelectedNode.Id, out var folder))
        {
            SelectedNode = folder;
        }
        else if (_treeViewModel.Nodes.TryGetValue(SelectedNode.Id, out var node))
        {
            SelectedNode = node;
        }
    }
}