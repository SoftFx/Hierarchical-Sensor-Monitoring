﻿using System;

namespace HSMServer.Model.TreeViewModels;

public record SearchPattern(string SearchParameter = "", bool IsSearchRefresh = false)
{
    private string _searchParameter;
    private bool _isMatchWord;
    
    
    public bool IsSearch => !string.IsNullOrEmpty(_searchParameter);
    
    public bool ShouldClearOpenedNodes => !IsSearchRefresh && IsSearch;
    
    
    public bool IsNameFits(string name) => _isMatchWord ? name.Equals(_searchParameter) : name.Contains(_searchParameter, StringComparison.OrdinalIgnoreCase);

    public void Recalculate(SearchPattern pattern)
    {
        _isMatchWord = pattern.SearchParameter is not null && pattern.SearchParameter.Length >= 2 && pattern.SearchParameter.StartsWith('"') && pattern.SearchParameter.EndsWith('"');
        _searchParameter = _isMatchWord ? pattern.SearchParameter[1..^1] : pattern.SearchParameter;
    }
}