using System;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.ViewModel;

public interface INodeChildrenViewModel
{
    public List<NodeViewModel> VisibleItems { get; }


    public string Title { get; }


    public int PageSize { get; }

    public int PageNumber { get; }


    public bool IsPaginationDisplayed { get; }
    
    public bool IsPageValid { get; }
}


public sealed class NodeChildrenViewModel<T> : INodeChildrenViewModel where T : NodeViewModel
{
    public List<NodeViewModel> VisibleItems => Items?.Values.OrderByDescending(n => n.Status)
                                                            .ThenBy(n => n.Name)
                                                            .Skip(PageNumber * PageSize)
                                                            .Take(PageSize)
                                                            .Select(x => (NodeViewModel)x).ToList();
    
    public IDictionary<Guid, T> Items { get; private set; } 
    
    
    public int PageSize { get; private set; } = 150;

    public int PageNumber { get; private set; } = 0;

    public int OriginalSize => Items.Count;

    
    public string Title { get; }
    

    public bool IsPaginationDisplayed => OriginalSize > PageSize;

    public bool IsPageValid => OriginalSize > PageNumber * PageSize && PageNumber >= 0;


    public NodeChildrenViewModel(string title)
    {
        Title = title;
    }


    public NodeChildrenViewModel<T> Load(IDictionary<Guid, T> collection)
    {
        if (collection is not null)
        {
            Items = collection;
            OriginalSize = Items.Count;
        }
        
        return this;
    }
    
    public void Reset()
    {
        PageNumber = 0;
        PageSize = 150;
    }
    
    public NodeChildrenViewModel<T> Reload(int pageNumber, int pageSize) => ChangePageNumber(pageNumber).ChangePageSize(pageSize);


    private NodeChildrenViewModel<T> ChangePageSize(int pageSize)
    {
        PageSize = pageSize <= 0 ? PageSize : pageSize;

        return this;
    }

    private NodeChildrenViewModel<T> ChangePageNumber(int pageNumber)
    {
        PageNumber = pageNumber < 0 ? PageNumber : pageNumber;

        return this;
    }
}