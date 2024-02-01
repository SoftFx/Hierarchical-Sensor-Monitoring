using System.Net.NetworkInformation;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    public sealed class ListenedSocketsSensor : SocketsSensor
    {
        protected override TcpState State => TcpState.Listen;

        
        public ListenedSocketsSensor(SocketSensorOptions options) : base(options) { }
        
        
        protected override int GetValue() => Properties.GetActiveTcpListeners().Length;
    }
}