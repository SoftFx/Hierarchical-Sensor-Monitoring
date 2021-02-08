using System;

namespace HSMClientWPFControls.Objects
{
    public class SensorHistoryItem
    {
        public DateTime Time { get; set; }
        public SensorTypes Type { get; set; }
        public byte[] SensorValue { get; set; }
    }
}
