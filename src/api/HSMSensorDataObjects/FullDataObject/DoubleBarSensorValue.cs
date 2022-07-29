using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;
using HSMSensorDataObjects.BarData;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class DoubleBarSensorValue : BarValueSensorBase<double>
    {
        [DataMember]
        [DefaultValue((int)SensorType.DoubleBarSensor)]
        public override SensorType Type => SensorType.DoubleBarSensor;

        [DataMember]
        [Obsolete]
        public List<PercentileValueDouble> Percentiles { get; set; }


        public DoubleBarSensorValue()
        {
            Percentiles = new List<PercentileValueDouble>();
        }
    }
}
