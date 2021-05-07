using HSMCommon.Model.SensorsData;
using HSMSensorDataObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMWebClient.Models
{
    public class SensorViewModel
    {
        public string Name { get; set; }

        public string Value { get; set; }

        public SensorType SensorType { get; set; }

        public SensorStatus Status { get; set; }

        public SensorViewModel(string name, SensorData sensor)
        {
            Name = name;
            SensorType = sensor.SensorType;
            Status = sensor.Status;
            Value = sensor.ShortValue;
        }
    }
}
