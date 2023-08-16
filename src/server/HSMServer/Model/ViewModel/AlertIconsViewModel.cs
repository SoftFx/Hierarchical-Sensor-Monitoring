using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.ViewModel;

public sealed class AlertIconsViewModel
{
    private const int MaxDisplayedSize = 2;
    private const int MaxBadgeCounterSize = 9;
    private const string InfiniteCharacter = "∞";
    
    public const int VisibleMaxSize = 3;


    private readonly ConcurrentDictionary<string, int> _alerts;


    public readonly bool ShowFullList;
    public readonly int VisibleCount;
    
    public IEnumerable<KeyValuePair<string, int>> VisibleIcons => _alerts.Take(VisibleCount);

    public bool IsTooLong => _alerts.Count > VisibleMaxSize && !ShowFullList;


    public AlertIconsViewModel(ConcurrentDictionary<string, int> alerts, bool showFullList = false)
    {
        _alerts = alerts;
        ShowFullList = showFullList;
        VisibleCount = IsTooLong ? MaxDisplayedSize : _alerts.Count;
    }
    
    public string GetLabelCount(int count) => count > MaxBadgeCounterSize ? InfiniteCharacter : $"{count}";
}