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

    public string CustomTitle { get; }


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

    public int OriginalSize => Items?.Count ?? 0;

    
    public string Title { get; }

    public string CustomTitle { get; private set; }
    

    public bool IsPaginationDisplayed => OriginalSize > PageSize;

    public bool IsPageValid => OriginalSize > PageNumber * PageSize && PageNumber >= 0;


    public NodeChildrenViewModel(string title)
    {
        Title = title;
    }


    public void Load(IDictionary<Guid, T> collection, string customTitle = null)
    {
        Items = collection ?? Items;
        CustomTitle = customTitle;
    }

    public void Reset()
    {
        PageNumber = 0;
        PageSize = 150;

        Items = null;
    }
    
    public NodeChildrenViewModel<T> Reload(ChildrenPageRequest pageRequest)
    {
        PageSize = pageRequest.PageSize <= 0 ? PageSize : pageRequest.PageSize;
        PageNumber = pageRequest.CurrentPage < 0 ? PageNumber : pageRequest.CurrentPage;

        return this;
    }
}