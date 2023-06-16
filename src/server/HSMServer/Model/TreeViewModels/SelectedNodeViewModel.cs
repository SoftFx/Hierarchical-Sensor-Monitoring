using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.ViewModel;

namespace HSMServer.Model.TreeViewModels;

public class SelectedNodeViewModel
{
    private BaseNodeViewModel _selectedNode;


    public string Id => _selectedNode?.Id.ToString();

    public bool HasChildren => Nodes.VisibleItems.Count + Sensors.VisibleItems.Count > 0;


    public NodeChildrenViewModel<SensorNodeViewModel> Sensors { get; } = new(nameof(Sensors));
  
    public NodeChildrenViewModel<ProductNodeViewModel> Nodes { get; } = new(nameof(Nodes));


    public void ConnectNode(ProductNodeViewModel newNode)
    {
        Subscribe(newNode);
        
        Nodes.Load(newNode.Nodes).SetCustomTitle(nameof(newNode.Nodes));
        Sensors.Load(newNode.Sensors).SetCustomTitle(nameof(newNode.Sensors));
    }

    public void ConnectFolder(FolderModel newFolder)
    {
        Subscribe(newFolder);

        Nodes.Load(newFolder.Products).SetCustomTitle(nameof(newFolder.Products));
        Sensors.Items?.Clear();
    }

    public INodeChildrenViewModel GetNextPage(ChildrenPageRequest pageRequest)
    {
        if (pageRequest.Id == "Nodes")
            return Nodes.Reload(pageRequest.CurrentPage, pageRequest.PageSize);
        
        return Sensors.Reload(pageRequest.CurrentPage, pageRequest.PageSize);
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