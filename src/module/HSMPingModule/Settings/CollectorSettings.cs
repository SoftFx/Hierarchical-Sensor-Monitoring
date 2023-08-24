namespace HSMPingModule.Settings;

internal sealed class CollectorSettings
{
    public string Key { get; set; }

    public string ServerAddress { get; set; } = "https://localhost";

    public int Port { get; set; } = 44333;
}