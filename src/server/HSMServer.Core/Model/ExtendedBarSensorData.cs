using System;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;

namespace HSMServer.Core.Model
{
    public class ExtendedBarSensorData
    {
        public BarSensorValueBase Value { get; set; }
        public SensorType ValueType { get; set; }
        public string ProductName { get; set; }
        public DateTime TimeCollected { get; set; }
    }
}
