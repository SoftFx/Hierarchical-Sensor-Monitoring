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

    
    public string Title { get; set; }
    

    public bool IsPaginationDisplayed => _isPaginated && OriginalSize > PageSize;

    public bool IsPageValid => OriginalSize <= PageNumber * PageSize || PageNumber < 0 || PageSize <= 0;


    public NodeChildrenViewModel(string title)
    {
        Title = title;
    }


    public NodeChildrenViewModel Load<T>(ICollection<T> collection) where T : NodeViewModel
    {
        if (collection is not null)
        {         
            VisibleItems.Clear();    
            VisibleItems.AddRange(collection.OrderByDescending(n => n.Status).ThenBy(n => n.Name).Skip(PageNumber * PageSize).Take(PageSize));
            _isPaginated = true;
            OriginalSize = collection.Count;
        }
        
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
        VisibleItems.Clear();
    }
}