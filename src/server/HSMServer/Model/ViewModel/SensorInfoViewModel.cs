using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Model;
using HSMServer.Helpers;
using HSMServer.Model.TreeViewModels;

namespace HSMServer.Model.ViewModel
{
    public class SensorInfoViewModel
    {
        public string Path { get; }

        public string ProductName { get; }

        public SensorType SensorType { get; }

        public string EncodedId { get; set; }

        public string Description { get; set; }

        public string ExpectedUpdateInterval { get; set; }

        public TimeIntervalViewModel Interval { get; set; }

        public string Unit { get; set; }


        // public constructor without parameters for action Home/UpdateSensorInfo
        public SensorInfoViewModel() { }

        public SensorInfoViewModel(SensorNodeViewModel sensor)
        {
            EncodedId = SensorPathHelper.EncodeGuid(sensor.Id);
            Path = sensor.Path;
            ProductName = sensor.Product;
            SensorType = sensor.SensorType;

            Description = sensor.Description;
            ExpectedUpdateInterval = sensor.ExpectedUpdateInterval.ToString();
            Unit = sensor.Unit;

            Interval = new();
        }


        internal SensorInfoViewModel Update(SensorUpdate updatedModel)
        {
            Description = updatedModel.Description;
            ExpectedUpdateInterval = updatedModel.ExpectedUpdateInterval.ToString();
            Unit = updatedModel.Unit;

            return this;
        }
    }
}
