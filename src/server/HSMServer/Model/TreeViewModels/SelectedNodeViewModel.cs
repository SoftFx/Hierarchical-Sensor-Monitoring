using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.ViewModel;

namespace HSMServer.Model.TreeViewModels;

public class SelectedNodeViewModel
{
    public BaseNodeViewModel SelectedNode { get; private set; }
    
    
    public NodeChildrenViewModel NodeChildrenSensors { get; } = new();
        
    public NodeChildrenViewModel NodeChildrenNodes { get; } = new();
    
    
    public void ConnectNode(BaseNodeViewModel newNode)
    {
        if (SelectedNode?.Id == newNode.Id)
            return;

        SelectedNode = newNode;
        
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
}