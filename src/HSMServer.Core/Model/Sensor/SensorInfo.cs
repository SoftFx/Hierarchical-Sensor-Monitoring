using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.Sensor
{
    public class SensorInfo
    {
        public string Path { get; set; }
        public string ProductName { get; set; }
        public string SensorName { get; set; }
        public string Description { get; set; }
        public SensorType SensorType { get; set; }
        public TimeSpan ExpectedUpdateInterval { get; set; }
        public List<SensorValidationParameter> ValidationParameters { get; set; }
        public string Unit { get; set; }
        public SensorInfo()
        {
            ValidationParameters = new List<SensorValidationParameter>();
        }
        public SensorInfo(SensorEntity entity)
        {
            if (entity == null) return;

            ProductName = entity.ProductName;
            Path = entity.Path;
            SensorName = entity.SensorName;
            Description = entity.Description;
            //ExpectedUpdateInterval = entity.ExpectedUpdateInterval;
            ExpectedUpdateInterval = new TimeSpan(entity.ExpectedUpdateIntervalTicks);
            Unit = entity.Unit;
            ValidationParameters = new List<SensorValidationParameter>();
            if (entity.ValidationParameters != null && entity.ValidationParameters.Any())
            {
                entity.ValidationParameters.ForEach(
                    p => ValidationParameters.Add(new SensorValidationParameter(p)));
            }
        }

        public void Update(SensorInfo sensorInfo)
        {
            Description = sensorInfo.Description;
            Unit = sensorInfo.Unit;
            ExpectedUpdateInterval = sensorInfo.ExpectedUpdateInterval;
        }
    }
}
