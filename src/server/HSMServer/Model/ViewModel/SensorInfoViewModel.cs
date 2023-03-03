﻿using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.ViewModel
{
    public class SensorInfoViewModel : NodeInfoBaseViewModel
    {
        public SensorType SensorType { get; }

        public string Unit { get; set; }


        // public constructor without parameters for action Home/UpdateSensorInfo
        public SensorInfoViewModel() : base() { }

        internal SensorInfoViewModel(SensorNodeViewModel sensor) : base(sensor)
        {
            SensorType = sensor.SensorType;
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
