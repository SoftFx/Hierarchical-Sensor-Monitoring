using System.Collections.Generic;
using HSMClient.Configuration;
using HSMClientWPFControls.Objects;
using MAMSClient.Configuration;

namespace HSMClient.ConnectionNode
{
    class SensorsMonitoringNode : MultiConnectionMonitoringNode
    {
        private List<SensorMonitoringInfo> _sensorsInfo;
        public SensorsMonitoringNode(string name, MonitoringNodeBase parent = null) : base(name, parent)
        {
        }

        public SensorsMonitoringNode(MachineInfo info, MonitoringNodeBase parent = null) : this($"jobs", parent)
        {
            _sensorsInfo = new List<SensorMonitoringInfo>();
            _sensorsInfo.AddRange(info.Sensors);
            foreach (var sensorInfo in SensorsInfo)
            {
                string address =
                    $"{ConfigProvider.Instance.ConnectionInfo.Address}:{ConfigProvider.Instance.ConnectionInfo.Port}";
                SubNodes.Add(new SensorMonitoringNode(sensorInfo.Name, address, sensorInfo, this));
            }
        }

        public List<SensorMonitoringInfo> SensorsInfo
        {
            get { return _sensorsInfo; }
        }

    }
}