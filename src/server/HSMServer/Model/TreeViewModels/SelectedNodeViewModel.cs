using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.ViewModel;

namespace HSMServer.Model.TreeViewModels;

public class SelectedNodeViewModel
{
    private readonly NodeChildrenViewModel<SensorNodeViewModel> _sensors = new("Sensors");
    private readonly NodeChildrenViewModel<ProductNodeViewModel> _nodes = new("Nodes");

    
    private BaseNodeViewModel _selectedNode;


    public string Id => _selectedNode?.Id.ToString();

    public bool HasChildren => _nodes.VisibleItems?.Count + _sensors.VisibleItems?.Count > 0;


    public void ConnectNode(ProductNodeViewModel newNode)
    {
        Subscribe(newNode);
        
        _nodes.Load(newNode.Nodes);
        _sensors.Load(newNode.Sensors);
    }

    public void ConnectFolder(FolderModel newFolder)
    {
        Subscribe(newFolder);

        _nodes.Load(newFolder.Products, "Products");
        _sensors.Reset();
    }

    public INodeChildrenViewModel GetNextPage(ChildrenPageRequest pageRequest) => pageRequest.IsNodes ? _nodes.Reload(pageRequest) : _sensors.Reload(pageRequest);
    

    private void Subscribe(BaseNodeViewModel newSelected)
    {
        if (_selectedNode?.Id == newSelected.Id)
            return;

        _selectedNode = newSelected;
        
        _nodes.Reset();
        _sensors.Reset();
    }
}