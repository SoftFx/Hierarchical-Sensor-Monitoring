using System.Net.NetworkInformation;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    public sealed class EstablishedSocketsSensor : SocketsSensor
    {
        protected override TcpState State => TcpState.Established;

        
        public EstablishedSocketsSensor(SocketSensorOptions options) : base(options) { }
    }
}