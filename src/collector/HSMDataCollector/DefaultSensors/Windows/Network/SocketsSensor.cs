using System.Net.NetworkInformation;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    public abstract class SocketsSensor : MonitoringSensorBase<int>
    {
        protected readonly IPGlobalProperties Properties = IPGlobalProperties.GetIPGlobalProperties();

        
        protected virtual TcpState State { get; }


        protected SocketsSensor(SocketSensorOptions options) : base(options)
        {
            State = options.State;
        }
    }
}