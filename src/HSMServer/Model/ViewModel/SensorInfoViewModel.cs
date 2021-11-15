using HSMServer.Core.Model.Sensor;

namespace HSMServer.Model.ViewModel
{
    public class SensorInfoViewModel
    {
        public string Path { get; set; }
        public string ProductName { get; set; }
        public string Description { get; set; }
        public string ExpectedUpdateInterval { get; set; }
        public string Unit { get; set; }
        public string SensorType { get; set; }
        public SensorInfoViewModel(SensorInfo info)
        {
            Path = info.Path;
            ProductName = info.ProductName;
            Description = info.Description;
            ExpectedUpdateInterval = info.ExpectedUpdateInterval.ToString();
            Unit = info.Unit;
            SensorType = info.SensorType.ToString();
        }

        public void Update(UpdateSensorInfoViewModel updateModel)
        {
            Description = updateModel.Description;
            ExpectedUpdateInterval = updateModel.ExpectedUpdateInterval;
            Unit = updateModel.Unit;
        }
    }
}
