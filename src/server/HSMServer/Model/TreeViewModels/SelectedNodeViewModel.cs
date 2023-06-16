using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.ViewModel;

namespace HSMServer.Model.TreeViewModels;

public class SelectedNodeViewModel
{
    private BaseNodeViewModel _selectedNode;


    public string Id => _selectedNode?.Id.ToString();

    public bool HasChildren => Nodes.VisibleItems?.Count + Sensors.VisibleItems?.Count > 0;


    public NodeChildrenViewModel<SensorNodeViewModel> Sensors { get; } = new(nameof(Sensors));
  
    public NodeChildrenViewModel<ProductNodeViewModel> Nodes { get; } = new(nameof(Nodes));


    public void ConnectNode(ProductNodeViewModel newNode)
    {
        Subscribe(newNode);
        
        Nodes.Load(newNode.Nodes);
        Sensors.Load(newNode.Sensors);
    }

    public void ConnectFolder(FolderModel newFolder)
    {
        Subscribe(newFolder);

        Nodes.Load(newFolder.Products, nameof(newFolder.Products));
        Sensors.Reset();
    }

    public INodeChildrenViewModel GetNextPage(ChildrenPageRequest pageRequest) => pageRequest.IsNodes ? Nodes.Reload(pageRequest) : Sensors.Reload(pageRequest);
    

    private void Subscribe(BaseNodeViewModel newSelected)
    {
        if (_selectedNode?.Id == newSelected.Id)
            return;

        _selectedNode = newSelected;
        
        Nodes.Reset();
        Sensors.Reset();
    }
}