using HSMClient.Configuration;
using HSMClientWPFControls.Objects;

namespace HSMClient.ConnectionNode
{
    class TTSMonitoringNode : MultiConnectionMonitoringNode
    {
        private TTSMonitoringInfo _ttsInfo;
        public TTSMonitoringNode(string name, MonitoringNodeBase parent = null) : base(name, parent)
        {

        }

        public TTSMonitoringNode(TTSMonitoringInfo ttsInfo, MonitoringNodeBase parent = null) : this(ttsInfo.Name,
            parent)
        {
            _ttsInfo = ttsInfo;
        }

        public TTSMonitoringInfo TTSInfo => _ttsInfo;
    }
}
