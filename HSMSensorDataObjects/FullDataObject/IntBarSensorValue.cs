using System.Collections.Generic;
using HSMSensorDataObjects.BarData;

namespace HSMSensorDataObjects.FullDataObject
{
    public class IntBarSensorValue : BarSensorValueBase
    {
        public int Min { get; set; }
        public int Max { get; set; }
        public int Mean { get; set; }
        public List<PercentileValueInt> Percentiles { get; set; }

        public IntBarSensorValue()
        {
            Percentiles = new List<PercentileValueInt>();
        }
    }
}
