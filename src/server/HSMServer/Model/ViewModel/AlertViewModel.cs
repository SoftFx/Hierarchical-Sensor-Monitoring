using System.Collections.Concurrent;

namespace HSMServer.Model.ViewModel;

public class AlertViewModel
{
    private const int MaxDisplayedSize = 2;
    private const int MaxSize = 3;

    public const int MaxBadgeCounterSize = 9;
    public const string InfiniteCharacter = "∞";


    public ConcurrentDictionary<string, int> Alerts { get; set; }

    public bool ShowFullList { get; set; }
    
    public int TakeNumber { get; init; }

    public bool IsTooLong => Alerts.Count > MaxSize && !ShowFullList;

    public AlertViewModel(ConcurrentDictionary<string, int> alerts, bool showFullList = false)
    {
        Alerts = alerts;
        ShowFullList = showFullList;
        TakeNumber = IsTooLong ? MaxDisplayedSize : Alerts.Count;
    }
}