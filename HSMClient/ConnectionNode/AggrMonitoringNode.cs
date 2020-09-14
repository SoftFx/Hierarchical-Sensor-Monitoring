using HSMClient.Configuration;
using HSMClientWPFControls.Objects;

namespace HSMClient.ConnectionNode
{
    class AggrMonitoringNode : ConnectionMonitoringNodeBase
    {
        private AggrMonitoringInfo _aggrInfo;
        public AggrMonitoringNode(string name, MonitoringNodeBase parent = null) : base(name, parent)
        {
        }

        public AggrMonitoringNode(AggrMonitoringInfo aggrInfo, MonitoringNodeBase parent = null) : this(aggrInfo.Name,
            parent)
        {
            _aggrInfo = aggrInfo;
        }

        public AggrMonitoringInfo AggrInfo => _aggrInfo;
    }
}
