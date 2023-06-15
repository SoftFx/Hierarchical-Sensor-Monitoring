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
                    return Nodes.Load(productNodeViewModel.Nodes.Values);

                return Nodes.Load((_selectedNode as FolderModel)?.Products.Values);
            case "Sensors":
                return Sensors.ChangePageNumber(pageNumber)
                              .ChangePageSize(pageSize)
                              .Load((_selectedNode as ProductNodeViewModel)?.Sensors.Values);
            default:
                return Nodes.ChangePageNumber(pageNumber)
                            .ChangePageSize(pageSize)
                            .Load((_selectedNode as FolderModel)?.Products.Values);;
        }
    }
    
    
    private void ReloadVisibleItems(BaseNodeViewModel node)
    {
        Reset();
        
        if (node is ProductNodeViewModel productNodeViewModel)
        {
            Nodes.Load(productNodeViewModel.Nodes.Values);
            Sensors.Load(productNodeViewModel.Sensors.Values);
        }
        else if (node is FolderModel folder)
            Nodes.Load(folder.Products.Values);
    }

    private void Reset()
    {
        Nodes.Reset();
        Sensors.Reset();
    }
}