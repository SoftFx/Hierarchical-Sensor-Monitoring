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

    public bool HasChildren => _nodes.VisibleItems?.Count > 0 || _sensors.VisibleItems?.Count > 0;

    /// <summary>
    /// True when the selected node has at least one group of >= 2 comparable child sensors to overlay
    /// on the node "Chart" tab (issue #1235). Set by the controller after the node is connected.
    /// </summary>
    public bool ShowChartTab { get; set; }


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

        // Reset whenever the selected node/folder changes (this instance is reused) so a previous node's
        // true can't leak onto the next selection; the node branch of SelectNode re-enables it, folders
        // leave it off. Selecting a sensor never calls this, but that's safe: the sensor panel
        // (_NodeDataPanel) doesn't render _ChildrenPanel — only _Node.cshtml / _Folder.cshtml do.
        ShowChartTab = false;

        _nodes.Reset();
        _sensors.Reset();
    }
}