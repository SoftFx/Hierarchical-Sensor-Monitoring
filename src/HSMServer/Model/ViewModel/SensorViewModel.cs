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
        public string Description { get; set; }

        public SensorViewModel(string name, SensorData sensor)
        {
            Name = name;
            SensorType = sensor.SensorType;
            Status = sensor.Status;
            Value = sensor.ShortValue;
            Description = sensor.Description;
        }

        public void Update(SensorData sensorData)
        {
            Status = sensorData.Status;
            Value = sensorData.ShortValue;
            Description = sensorData.Description;
        }

        public void Update(SensorViewModel viewModel)
        {
            Status = viewModel.Status;
            Value = viewModel.Value;
            Description = viewModel.Description;
        }
    }
}
