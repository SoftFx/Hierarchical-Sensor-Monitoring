using System.Collections.Generic;
using HSMSensorDataObjects.BarData;

namespace HSMSensorDataObjects.FullDataObject
{
    public class DoubleBarSensorValue : BarSensorValueBase
    {
        public double Min { get; set; }
        public double Max { get; set; }
        public double Mean { get; set; }
        public List<PercentileValueDouble> Percentiles { get; set; }

        public DoubleBarSensorValue()
        {
            Percentiles = new List<PercentileValueDouble>();
        }
    }
}
