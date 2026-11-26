using HSMServer.Model.Folders;
using System;
using System.Collections.Generic;
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


    // One user may have several requests in flight (two browser tabs). Selecting an entity and loading
    // its children is one step: a refresh that checked the selection must not write its list after
    // another request has selected something else.
    private readonly object _selection = new();

    public void ConnectNode(ProductNodeViewModel newNode)
    {
        lock (_selection)
        {
            Subscribe(newNode);

            _nodes.Load(newNode.Nodes);
            _sensors.Load(newNode.Sensors);
        }
    }

    /// <param name="newFolder">The selected folder.</param>
    /// <param name="visibleProducts">
    /// The products of the folder to page through: the ones the user can see, which is not always the whole
    /// folder (<see cref="UserTreeAccess.GetVisibleProducts"/>).
    /// </param>
    public void ConnectFolder(FolderModel newFolder, IDictionary<Guid, ProductNodeViewModel> visibleProducts)
    {
        lock (_selection)
        {
            Subscribe(newFolder);

            _nodes.Load(visibleProducts, "Products");
            _sensors.Reset();
        }
    }

    /// <summary>
    /// Replaces the product list of the selected folder with the products the user sees now. Does nothing
    /// when another entity has been selected in the meantime: a page request never changes the selection.
    /// </summary>
    public void RefreshFolderProducts(Guid folderId, IDictionary<Guid, ProductNodeViewModel> visibleProducts)
    {
        lock (_selection)
        {
            if (_selectedNode?.Id == folderId)
                _nodes.Load(visibleProducts, "Products");
        }
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