using HSMServer.Model.TreeViewModels;
using System;

namespace HSMServer.Model.ViewModel
{
    public class SensorInfoViewModel
    {
        public Guid Id { get; }

        public string Path { get; }

        public string ProductName { get; }

        public string SensorType { get; }

        public string Description { get; private set; }

        public string ExpectedUpdateInterval { get; private set; }

        public string Unit { get; private set; }

        public SensorInfoViewModel(SensorNodeViewModel sensor)
        {
            Id = sensor.Id;
            Path = sensor.Path;
            ProductName = sensor.Product;
            Description = sensor.Description;
            ExpectedUpdateInterval = sensor.ExpectedUpdateInterval.ToString();
            Unit = sensor.Unit;
            SensorType = sensor.SensorType.ToString();
        }

        public void Update(UpdateSensorInfoViewModel updateModel)
        {
            Description = updateModel.Description;
            ExpectedUpdateInterval = updateModel.ExpectedUpdateInterval;
            Unit = updateModel.Unit;
        }
    }
}
