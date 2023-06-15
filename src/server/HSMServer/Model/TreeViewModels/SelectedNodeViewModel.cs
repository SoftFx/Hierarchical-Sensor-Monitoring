using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.ViewModel;

namespace HSMServer.Model.TreeViewModels;

public class SelectedNodeViewModel
{
    private BaseNodeViewModel _selectedNode;


    public string Id => _selectedNode?.Id.ToString();
    
    
    public NodeChildrenViewModel Sensors { get; } = new(nameof(Sensors));
        
    public NodeChildrenViewModel Nodes { get; } = new(nameof(Nodes));
    
    
    public void ConnectNode(ProductNodeViewModel newNode)
    {
        Subscribe(newNode);
        
        Nodes.Load(newNode.Nodes.Values);
        Sensors.Load(newNode.Sensors.Values);
    }

    public void ConnectFolder(FolderModel newFolder)
    {
        Subscribe(newFolder);

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
                            .Load((_selectedNode as FolderModel)?.Products.Values);
        }
    }

    
    private void Subscribe(BaseNodeViewModel newSelected)
    {
        if (_selectedNode?.Id == newSelected.Id)
            return;

        _selectedNode = newSelected;
        
        Nodes.Reset();
        Sensors.Reset();
    }
}