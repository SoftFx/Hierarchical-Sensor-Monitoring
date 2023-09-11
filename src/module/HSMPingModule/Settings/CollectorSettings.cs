namespace HSMPingModule.Settings;

internal sealed class CollectorSettings
{
    public string Key { get; set; } = string.Empty;

    public string ServerAddress { get; set; } = "localhost";

    public int Port { get; set; } = 44333;
}