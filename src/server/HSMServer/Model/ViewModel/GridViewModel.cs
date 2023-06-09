using System.Collections.Generic;
using System.Linq;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.ViewModel;

public sealed class GridViewModel
{
    public List<NodeViewModel> VisibleItems { get; set; } = new();
    
    public int PageSize { get; init; } = 1000;
    
    public int PageNumber { get; init; } = 0;

    public bool IsSensorGrid { get; set; }
    
    
    public GridViewModel(ProductNodeViewModel productNodeViewModel, int pageNumber, int pageSize)
    {
        PageNumber = pageNumber;
        PageSize = pageSize;
        IsSensorGrid = true;
        
        VisibleItems = new List<NodeViewModel>(productNodeViewModel.Sensors.Values.Where(n => n.HasData)
            .OrderByDescending(n => n.Status)
            .ThenBy(n => n.Name)
            .Skip(PageNumber * PageSize)
            .Take(PageSize));
    }

    public GridViewModel(bool isSensorGrid)
    {
        IsSensorGrid = isSensorGrid;
    }
    
    public GridViewModel() { }
}