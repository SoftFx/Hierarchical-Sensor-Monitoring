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
    
    
    public void ConnectNode(ProductNodeViewModel newNode)
    {
        if (_selectedNode?.Id == newNode.Id)
            return;

        _selectedNode = newNode;
        
        Reset();
        
        Nodes.Load(newNode.Nodes.Values);
        Sensors.Load(newNode.Sensors.Values);
    }

    public void ConnectFolder(FolderModel newFolder)
    {
        if (_selectedNode?.Id == newFolder.Id)
            return;

        _selectedNode = newFolder;
        
        Reset();
        
        Nodes.Load(newFolder.Products.Values);
    }

    public NodeChildrenViewModel ReloadPage(ChildrenPageRequest pageRequest)
    {
        switch (pageRequest.Id)
        {
            case "Nodes":
                Nodes.ChangePageNumber(pageRequest.CurrentPage).ChangePageSize(pageRequest.PageSize);
                
                if (_selectedNode is ProductNodeViewModel productNodeViewModel)
                    return Nodes.Load(productNodeViewModel.Nodes.Values);

                return Nodes.Load((_selectedNode as FolderModel)?.Products.Values);
            case "Sensors":
                return Sensors.ChangePageNumber(pageRequest.CurrentPage)
                              .ChangePageSize(pageRequest.PageSize)
                              .Load((_selectedNode as ProductNodeViewModel)?.Sensors.Values);
            default:
                return Nodes.ChangePageNumber(pageRequest.CurrentPage)
                            .ChangePageSize(pageRequest.PageSize)
                            .Load((_selectedNode as FolderModel)?.Products.Values);;
        }
    }

    private void Reset()
    {
        Nodes.Reset();
        Sensors.Reset();
    }
}