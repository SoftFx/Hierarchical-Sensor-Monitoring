using HSMCommon.Model.SensorsData;
using HSMSensorDataObjects;

namespace HSMServer.Model.ViewModel
{
    public class SensorViewModel
    {
        //ToDo: add path
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
