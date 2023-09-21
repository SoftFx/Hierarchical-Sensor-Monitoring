namespace HSMPingModule.Settings;

internal sealed class NodeSettings
{
    public List<string> Countries { get; set; }


    public TimeSpan? PingThresholdValue { get; set; }

    public TimeSpan? TTL { get; set; }
}