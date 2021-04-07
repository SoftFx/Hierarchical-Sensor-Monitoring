using System.Collections.Generic;
using HSMClientWPFControls.Objects;

namespace HSMClientWPFControls.ConnectorInterface
{
    public interface ISensorHistoryConnector
    {
        public List<SensorHistoryItem> GetSensorHistory(string product, string path, string name, long n);
    }
}