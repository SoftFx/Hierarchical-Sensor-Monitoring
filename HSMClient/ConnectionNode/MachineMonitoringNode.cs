using System.Linq;
using HSMClientWPFControls.Objects;
using MAMSClient.Configuration;

namespace HSMClient.ConnectionNode
{
    class MachineMonitoringNode : MonitoringNodeBase
    {
        private MachineInfo _machineInfo;
        private readonly ClientMonitoringModel _model;
        public MachineMonitoringNode(MachineInfo machineInfo, ClientMonitoringModel model,
            MonitoringNodeBase parent = null) : base(machineInfo.Name, parent)
        {
            _machineInfo = machineInfo;
            _model = model;
            if (MachineInfo.Sensors?.Any() == true)
            {
                SubNodes.Add(new SensorsMonitoringNode(machineInfo, this));
            }

            if (MachineInfo.AggrMonitoringInfo != null)
            {
                SubNodes.Add(new AggrMonitoringNode(MachineInfo.AggrMonitoringInfo, this));
            }

            if (MachineInfo.TTSMonitoringInfo != null)
            {
                SubNodes.Add(new TTSMonitoringNode(MachineInfo.TTSMonitoringInfo, this));
            }
        }

        public MachineInfo MachineInfo => _machineInfo;
    }
}
