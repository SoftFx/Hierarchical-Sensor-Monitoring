namespace HSMPingModule.Settings;


internal sealed class PingSettings
{
    public TimeSpan RequestsPeriod { get; set; } = TimeSpan.FromMinutes(5);

    public bool UseNordVpn { get; set; }
}