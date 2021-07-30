using HSMCommon.Model;
using HSMCommon.Model.SensorsData;
using HSMSensorDataObjects;
using System;

namespace HSMServer.Model.ViewModel
{
    public class SensorViewModel
    {
        //ToDo: add path
        public string Name { get; set; }
        public string StringValue { get; set; }
        public SensorType SensorType { get; set; }
        public SensorStatus Status { get; set; }
        public string Description { get; set; }
        public DateTime Time { get; set; }
        public string ShortStringValue { get; set; }
        public TransactionType TransactionType { get; set; }
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
        }

        public void Update(SensorData sensorData)
        {
            Status = sensorData.Status;
            StringValue = sensorData.StringValue;
            ShortStringValue = sensorData.ShortStringValue;
            Description = sensorData.Description;
            Time = sensorData.Time;
            TransactionType = sensorData.TransactionType;
        }

        public void Update(SensorViewModel viewModel)
        {
            Status = viewModel.Status;
            StringValue = viewModel.StringValue;
            ShortStringValue = viewModel.ShortStringValue;
            Description = viewModel.Description;
            Time = viewModel.Time;
            TransactionType = viewModel.TransactionType;
        }
    }
}
