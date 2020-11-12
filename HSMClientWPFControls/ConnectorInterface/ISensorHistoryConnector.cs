using System.Collections.Generic;
using HSMClientWPFControls.Objects;

namespace HSMClientWPFControls.ConnectorInterface
{
    public interface ISensorHistoryConnector
    {
        public List<MonitoringSensorUpdate> GetSensorHistory(string product, string name, long n);
    }
}