using System;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.TreeViewModels;

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
    private IDictionary<Guid, T> _items;


    public List<NodeViewModel> VisibleItems => _items?.Values.OrderByDescending(n => n.Status)
                                                            .ThenBy(n => n.Name)
                                                            .Skip(PageNumber * PageSize)
                                                            .Take(PageSize)
                                                            .Select(x => (NodeViewModel)x).ToList();


    public int PageSize { get; private set; } = 169;

    public int PageNumber { get; private set; } = 0;

    public int OriginalSize => _items?.Count ?? 0;

    
    public string Title { get; set; }


    public bool IsPaginationDisplayed => OriginalSize > PageSize;

    public bool IsPageValid => OriginalSize > PageNumber * PageSize && PageNumber >= 0;


    public NodeChildrenViewModel(string title)
    {
        Title = title;
    }


    public void Load(IDictionary<Guid, T> collection)
    {
        _items = collection ?? _items;
    }

    public void Reset()
    {
        PageNumber = 0;
        PageSize = 169;

        _items = null;
    }
    
    public NodeChildrenViewModel<T> Reload(ChildrenPageRequest pageRequest)
    {
        PageSize = pageRequest.PageSize <= 0 ? PageSize : pageRequest.PageSize;
        PageNumber = pageRequest.CurrentPage < 0 ? PageNumber : pageRequest.CurrentPage;

        return this;
    }
}