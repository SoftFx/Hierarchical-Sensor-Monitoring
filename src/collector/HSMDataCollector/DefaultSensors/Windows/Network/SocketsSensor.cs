using System.Linq;
using System.Net.NetworkInformation;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    public abstract class SocketsSensor : MonitoringSensorBase<int>
    {
        private readonly IPGlobalProperties _properties = IPGlobalProperties.GetIPGlobalProperties();

        
        protected virtual TcpState State { get; }
        
        
        public SocketsSensor(SocketSensorOptions options) : base(options)
        {
            State = options.State;
        }

        
        protected override int GetValue() => _properties.GetActiveTcpConnections().Count(x => State == x.State);
    }
}