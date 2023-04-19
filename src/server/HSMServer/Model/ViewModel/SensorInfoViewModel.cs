using System;
using HSMServer.Core.Model;
using HSMServer.Model.TreeViewModel;
using SensorStatus = HSMServer.Model.TreeViewModel.SensorStatus;

namespace HSMServer.Model.ViewModel
{
    public class SensorInfoViewModel : NodeInfoBaseViewModel
    {
        public SensorType SensorType { get; }
        
       

        public string Unit { get; set; }

        public string StatusComment { get; set; }
        
        public string Comment { get; set; }

        public string ShortLastValue { get; set; }

        
        // public constructor without parameters for action Home/UpdateSensorInfo
        public SensorInfoViewModel() : base() { }

        internal SensorInfoViewModel(SensorNodeViewModel sensor) : base(sensor)
        {
            SensorType = sensor.Type;
            Status = sensor.Status;
            Unit = sensor.Unit;
            StatusComment = sensor.ValidationError;
            Comment = sensor.LastValue?.Comment;
            LastUpdateTime = sensor.UpdateTime;
            ShortLastValue = sensor.ShortStringValue;
        }
    }
}
