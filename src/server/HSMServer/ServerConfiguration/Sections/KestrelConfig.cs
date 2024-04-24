namespace HSMServer.ServerConfiguration;

public class KestrelConfig
{
    public const int DefaultSensorPort = 44330;
    public const int DefaultSitePort = 44333;


    public int SensorPort { get; set; } = DefaultSensorPort;

    public int SitePort { get; set; } = DefaultSitePort;
}
