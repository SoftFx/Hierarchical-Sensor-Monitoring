using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.ViewModel;

namespace HSMServer.Model.TreeViewModels;

public class SelectedNodeViewModel
{
    private BaseNodeViewModel _selectedNode;


    public string Id => _selectedNode?.Id.ToString();
    
    public NodeChildrenViewModel Sensors { get; } = new();
        
    public NodeChildrenViewModel Nodes { get; } = new();
    
    
    public void ConnectNode(BaseNodeViewModel newNode)
    {
        if (_selectedNode?.Id == newNode.Id)
            return;

        _selectedNode = newNode;
        
        ReloadVisibleItems(_selectedNode);
    }

    public NodeChildrenViewModel ReloadPage(string accordionId, int pageNumber, int pageSize)
    {
        switch (accordionId)
        {
            case "Nodes":
                Nodes.ChangePageNumber(pageNumber).ChangePageSize(pageSize);
                
                if (_selectedNode is ProductNodeViewModel productNodeViewModel)
                    return Nodes.InitializeItems(productNodeViewModel.Nodes.Values).TurnOnPagination();

                return Nodes.InitializeItems((_selectedNode as FolderModel)?.Products.Values).TurnOnPagination();
            case "Sensors":
                return Sensors.ChangePageNumber(pageNumber)
                                          .ChangePageSize(pageSize)
                                          .InitializeItems((_selectedNode as ProductNodeViewModel)?.Sensors.Values).TurnOnPagination();
            default:
                return Nodes.ChangePageNumber(pageNumber)
                                        .ChangePageSize(pageSize)
                                        .InitializeItems((_selectedNode as FolderModel)?.Products.Values).TurnOnPagination();;
        }
    }
    
    
    private void ReloadVisibleItems(BaseNodeViewModel node)
    {
        Reset();
        
        if (node is ProductNodeViewModel productNodeViewModel)
        {
            Nodes.InitializeItems(productNodeViewModel.Nodes.Values).TurnOnPagination();
            Sensors.InitializeItems(productNodeViewModel.Sensors.Values).TurnOnPagination();
        }
        else if (node is FolderModel folder)
            Nodes.InitializeItems(folder.Products.Values).TurnOnPagination();
    }

    private void Reset()
    {
        Nodes.Reset();
        Sensors.Reset();
    }
}