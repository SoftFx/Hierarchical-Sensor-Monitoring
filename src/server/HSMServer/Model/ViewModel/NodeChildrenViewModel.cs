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

    bool IsPageAvailable(int pageNumber);
}

public sealed class NodeChildrenViewModel<T> : INodeChildrenViewModel where T : NodeViewModel
{
    private readonly string _originTitle;

    private IDictionary<Guid, T> _items;


    public List<NodeViewModel> VisibleItems => _items?.Values.OrderByDescending(n => n.Status)
                                                             .ThenBy(n => n.Name)
                                                             .Skip(PageNumber * PageSize)
                                                             .Take(PageSize)
                                                             .Select(x => (NodeViewModel)x).ToList();

    public string Title { get; private set; }


    public int PageSize { get; private set; } = 168;

    public int PageNumber { get; private set; } = 0;

    public int OriginalSize => _items?.Count ?? 0;


    public bool IsPaginationDisplayed => OriginalSize > PageSize;

    public bool IsPageValid => IsPageAvailable(PageNumber);


    public NodeChildrenViewModel(string title)
    {
        _originTitle = title;

        Title = title;
    }


    public void Load(IDictionary<Guid, T> collection, string customTitle = null)
    {
        _items = collection ?? _items;

        Title = customTitle ?? _originTitle;
    }

    public void Reset()
    {
        Title = _originTitle;

        PageNumber = 0;
        PageSize = 168;

        _items = null;
    }

    public NodeChildrenViewModel<T> Reload(ChildrenPageRequest pageRequest)
    {
        PageSize = pageRequest.PageSize <= 0 ? PageSize : pageRequest.PageSize > 1000 ? 1000 : pageRequest.PageSize;
        PageNumber = pageRequest.CurrentPage < 0 ? PageNumber : IsPageAvailable(pageRequest.CurrentPage) ? pageRequest.CurrentPage : 0;

        return this;
    }

    public bool IsPageAvailable(int pageNumber) => OriginalSize > pageNumber * PageSize && pageNumber >= 0;
}