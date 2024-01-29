using System.Net.NetworkInformation;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    public sealed class EstablishedSocketsSensor : SocketsSensor
    {
        public EstablishedSocketsSensor(SocketSensorOptions options) : base(options)
        {
            options.State = TcpState.Established;
        }
    }
}