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

        public TimeIntervalViewModel ExpectedUpdateInterval { get; set; }

        public string Description { get; set; }

        public string Unit { get; set; }


        // public constructor without parameters for action Home/UpdateSensorInfo
        public SensorInfoViewModel() { }

        public SensorInfoViewModel(SensorNodeViewModel sensor)
        {
            EncodedId = SensorPathHelper.EncodeGuid(sensor.Id);
            Path = $"/{sensor.Path}";
            ProductName = sensor.Product;
            SensorType = sensor.SensorType;

            ExpectedUpdateInterval = sensor.ExpectedUpdateInterval;
            Description = sensor.Description;
            Unit = sensor.Unit;
        }


        internal SensorInfoViewModel Update(SensorUpdate updatedModel)
        {
            ExpectedUpdateInterval = new TimeIntervalViewModel(updatedModel.ExpectedUpdateInterval);
            Description = updatedModel.Description;
            Unit = updatedModel.Unit;

            return this;
        }
    }
}
