using System.Linq;
using System.Net.NetworkInformation;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    public class SocketsSensor : MonitoringSensorBase<int>
    {
        private readonly IPGlobalProperties _properties = IPGlobalProperties.GetIPGlobalProperties();
        private readonly TcpState _state;
        
        
        public SocketsSensor(SocketSensorOptions options) : base(options)
        {
            _state = options.State;
        }

        
        protected override int GetValue() => _properties.GetActiveTcpConnections().Count(x => _state == x.State);
    }
}