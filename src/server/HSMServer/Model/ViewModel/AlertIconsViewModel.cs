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
    private readonly bool _showFullList;
    private readonly int _visibleCount;


    public IEnumerable<KeyValuePair<string, int>> VisibleIcons => _alerts.Take(_visibleCount);

    public bool IsTooLong => _alerts.Count > VisibleMaxSize && !_showFullList;


    public AlertIconsViewModel(ConcurrentDictionary<string, int> alerts, bool showFullList = false)
    {
        _alerts = alerts;
        _showFullList = showFullList;
        _visibleCount = IsTooLong ? MaxDisplayedSize : _alerts.Count;
    }


    public string GetLabelCount(int count) => count > MaxBadgeCounterSize ? InfiniteCharacter : $"{count}";
}