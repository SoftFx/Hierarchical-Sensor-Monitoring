using System.Net.NetworkInformation;
using HSMDataCollector.Options;
using System.Linq;

namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    public sealed class EstablishedSocketsSensor : SocketsSensor
    {
        protected override TcpState State => TcpState.Established;

        
        public EstablishedSocketsSensor(SocketSensorOptions options) : base(options) { }
        
        
        protected override int GetValue() => Properties.GetActiveTcpConnections().Count(x => State == x.State);
    }
}