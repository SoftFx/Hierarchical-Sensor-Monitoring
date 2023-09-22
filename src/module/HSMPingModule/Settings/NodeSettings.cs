namespace HSMPingModule.Settings;

internal sealed class NodeSettings
{
    public HashSet<string> Countries { get; set; }


    public double? PingThresholdValueSec { get; set; }

    public TimeSpan? TTL { get; set; }
}