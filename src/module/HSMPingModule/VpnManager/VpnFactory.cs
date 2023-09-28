using HSMPingModule.Settings;

namespace HSMPingModule.VpnManager
{
    internal static class VpnFactory
    {
        internal static BaseVpnManager GetVpn(PingSettings settings)
        {
            return settings.UseNordVpn ? new NordVpnManager() : new NoVpnManager();
        }
    }
}