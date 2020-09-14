using System.Collections.Generic;

namespace HSMClientWPFControls.UpdateObjects
{
    public class MonitoringNodeUpdate
    {
        public string Name { get; set; }

        public List<MonitoringNodeUpdate> SubNodes { get; set; }
        public List<MonitoringCounterUpdate> Counters { get; set; }
    }
}
