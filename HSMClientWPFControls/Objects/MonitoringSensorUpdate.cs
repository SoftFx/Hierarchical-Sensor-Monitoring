using System.Collections.Generic;

namespace HSMClientWPFControls.Objects
{
    public class MonitoringSensorUpdate
    {
        public string Name { get; set; }
        public string Product { get; set; }
        public List<string> Path { get; set; }
        public ActionTypes ActionType { get; set; }
        public SensorTypes SensorType { get; set; }
        public byte[] DataObject { get; set; }
    }
}