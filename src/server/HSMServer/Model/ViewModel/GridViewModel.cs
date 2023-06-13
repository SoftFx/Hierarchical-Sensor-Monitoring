using System.Collections.Generic;
using System.Linq;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.ViewModel;

public sealed class GridViewModel
{
    public List<NodeViewModel> VisibleItems { get; set; } = new();
    
    public int PageSize { get; init; } = 1000;
    
    public int PageNumber { get; init; } = 0;

    public bool IsPaginated { get; set; } = false;


    public GridViewModel() { }

    public GridViewModel(int pageNumber, int pageSize)
    {
        PageNumber = pageNumber;
        PageSize = pageSize;
        IsPaginated = true;
    }

    public GridViewModel InitializeItems<T>(ICollection<T> collection) where T : NodeViewModel
    {
        VisibleItems = new List<NodeViewModel>(collection.Where(n => n.HasData).OrderByDescending(n => n.Status).ThenBy(n => n.Name)
            .Skip(PageNumber * PageSize).Take(PageSize));

        return this;
    }

    
    public GridViewModel TurnOnPagination()
    {
        IsPaginated = true;

        return this;
    }
}