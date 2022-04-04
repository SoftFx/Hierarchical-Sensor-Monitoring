using HSMSensorDataObjects;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.TreeValuesCache.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace HSMServer.Model.TreeViewModels
{
    public class SensorViewModel : NodeViewModel
    {
        public string StringValue { get; set; }
        public SensorType SensorType { get; set; }
        public string Description { get; set; }
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
            UpdateTime = model.LastUpdateTime;
            ShortStringValue = JsonSerializer.Deserialize<BoolSensorData>(model.TypedData).BoolValue.ToString(); // TODO: build ShortStringValue and StringValue for all sensors 
        }
    }
}
