using System;
using System.Collections.Generic;
using HSMSensorDataObjects;

namespace HSMClientWPFControls.Objects
{
    public class MonitoringSensorUpdate
    {
        public string Name { get; set; }
        public string Product { get; set; }
        public List<string> Path { get; set; }
        public ActionTypes ActionType { get; set; }
        public SensorTypes SensorType { get; set; }
        public DateTime Time { get; set; }
        public string ShortValue { get; set; }
        public SensorStatus Status { get; set; }
    }
}