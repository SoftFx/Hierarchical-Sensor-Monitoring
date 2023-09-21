namespace HSMPingModule.Settings;

internal sealed class NodeSettings
{
    public List<string> Countries { get; set; }


    public int? PingThresholdValue { get; set; }

    public TimeSpan? TTL { get; set; }
}