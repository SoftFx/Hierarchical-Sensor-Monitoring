using System.Collections.Generic;
using HSMClientWPFControls.Objects;

namespace HSMClientWPFControls.ConnectorInterface
{
    public interface ISensorsTreeConnector
    {
        public List<MonitoringSensorUpdate> GetTree();
        public List<MonitoringSensorUpdate> GetUpdates();
    }
}