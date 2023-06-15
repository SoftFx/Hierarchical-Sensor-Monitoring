using System.Collections.Generic;
using System.Linq;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.ViewModel;

public sealed class NodeChildrenViewModel
{
    private bool _isPaginated = false;
    
    
    public List<NodeViewModel> VisibleItems { get; set; } = new(1 << 8);
    
    
    public int PageSize { get; private set; } = 150;

    public int PageNumber { get; private set; } = 0;

    public int OriginalSize { get; private set; } = 0;


    public bool IsPaginationDisplayed => _isPaginated && OriginalSize > PageSize;
    
    
    public NodeChildrenViewModel() { }

    public NodeChildrenViewModel(int pageNumber, int pageSize)
    {
        PageNumber = pageNumber;
        PageSize = pageSize;
        _isPaginated = true;
    }

    
    public NodeChildrenViewModel InitializeItems<T>(ICollection<T> collection) where T : NodeViewModel
    {
        VisibleItems.Clear();
        VisibleItems.AddRange(collection.OrderByDescending(n => n.Status).ThenBy(n => n.Name)
            .Skip(PageNumber * PageSize).Take(PageSize));

        OriginalSize = collection.Count;
        
        return this;
    }
    
    public NodeChildrenViewModel TurnOnPagination()
    {
        _isPaginated = true;

        return this;
    }

    public NodeChildrenViewModel ChangePageSize(int pageSize)
    {
        PageSize = pageSize <= 0 ? PageSize : pageSize;

        return this;
    }

    public NodeChildrenViewModel ChangePageNumber(int pageNumber)
    {
        PageNumber = pageNumber < 0 ? PageNumber : pageNumber;

        return this;
    }

    public void Reset()
    {
        PageNumber = 0;
        PageSize = 150;
    }
}