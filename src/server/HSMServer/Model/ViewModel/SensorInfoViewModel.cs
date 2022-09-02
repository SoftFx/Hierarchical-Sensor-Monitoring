using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Model;
using HSMServer.Helpers;
using HSMServer.Model.TreeViewModels;
using System.Collections.Generic;

namespace HSMServer.Model.ViewModel
{
    public class SensorInfoViewModel
    {
        private readonly List<TimeInterval> _predefinedIntervals =
            new()
            {
                TimeInterval.None,
                TimeInterval.TenMinutes,
                TimeInterval.Hour,
                TimeInterval.Day,
                TimeInterval.Week,
                TimeInterval.Month,
                TimeInterval.Custom
            };


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

            ExpectedUpdateInterval = new(sensor.ExpectedUpdateInterval.ToModel(), _predefinedIntervals);
            Description = sensor.Description;
            Unit = sensor.Unit;
        }


        internal SensorInfoViewModel Update(SensorUpdate updatedModel)
        {
            ExpectedUpdateInterval = new(updatedModel.ExpectedUpdateInterval, _predefinedIntervals);
            Description = updatedModel.Description;
            Unit = updatedModel.Unit;

            return this;
        }
    }
}
