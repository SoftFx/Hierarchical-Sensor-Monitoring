using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;

namespace HSMPingModule.SensorStructure
{
    internal sealed class ApplicationNode
    {
        private const string ApplicationNodeName = "Application";
        private const string ExceptionSensorName = "Exceptions";
        private const string VpnStatusSensorName = "Vpn status";

        private readonly IDataCollector _collector;
        private IInstantValueSensor<bool> _vpnStatus;


        internal IInstantValueSensor<string> Exceptions { get; }


        internal ApplicationNode(IDataCollector collector)
        {
            _collector = collector;

            Exceptions = _collector.CreateStringSensor(GetAppPath(ExceptionSensorName));
        }


        internal void SendVpnStatus(bool ok, string description, string error = null)
        {
            _vpnStatus = _collector.CreateBoolSensor(GetAppPath(VpnStatusSensorName), description);

            _vpnStatus.AddValue(ok, ok ? SensorStatus.Ok : SensorStatus.Error, error);
        }


        private static string GetAppPath(string sensor) => $"{ApplicationNodeName}/{sensor}";
    }
}