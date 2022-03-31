using HSMSensorDataObjects;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.TreeValuesCache.Entities;
using System;

namespace HSMServer.Model.ViewModel
{
    public class SensorViewModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string StringValue { get; set; }
        public SensorType SensorType { get; set; }
        public SensorStatus Status { get; set; }
        public string Description { get; set; }
        public DateTime Time { get; set; }
        public string ShortStringValue { get; set; }
        public TransactionType TransactionType { get; set; }
        public string ValidationError { get; set; }

        
        public SensorViewModel(SensorModel model)
        {
            Id = model.Id.ToString();
            Name = model.SensorName;
            SensorType = model.SensorType;
            Status = model.Status;
            Description = model.Description;
            Time = model.LastUpdateTime;
        }

        public SensorViewModel(string name, SensorData sensor)
        {
            Name = name;
            SensorType = sensor.SensorType;
            Status = sensor.Status;
            StringValue = sensor.StringValue;
            ShortStringValue = sensor.ShortStringValue;
            Description = sensor.Description;
            Time = sensor.Time;
            TransactionType = sensor.TransactionType;
            ValidationError = sensor.ValidationError;
        }

        public SensorViewModel() { }

        public void Update(SensorData sensorData)
        {
            Status = sensorData.Status;
            StringValue = sensorData.StringValue;
            ShortStringValue = sensorData.ShortStringValue;
            Description = sensorData.Description;
            Time = sensorData.Time;
            TransactionType = sensorData.TransactionType;
            ValidationError = sensorData.ValidationError;
        }

        public void Update(SensorViewModel viewModel)
        {
            Status = viewModel.Status;
            StringValue = viewModel.StringValue;
            ShortStringValue = viewModel.ShortStringValue;
            Description = viewModel.Description;
            Time = viewModel.Time;
            TransactionType = viewModel.TransactionType;
            ValidationError = viewModel.ValidationError;
        }

        public SensorViewModel Clone()
        {
            var sensor = new SensorViewModel();
            sensor.Name = Name;
            sensor.SensorType = SensorType;
            sensor.Status = Status;
            sensor.StringValue = StringValue;
            sensor.ShortStringValue = ShortStringValue;
            sensor.Description = Description;
            sensor.Time = Time;
            sensor.TransactionType = TransactionType;
            sensor.ValidationError = ValidationError;

            return sensor;
        }
    }
}
