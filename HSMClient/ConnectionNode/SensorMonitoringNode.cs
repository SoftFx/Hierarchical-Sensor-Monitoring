using System.Collections.Generic;
using System.Text.Json;
using HSMClient.Common;
using HSMClient.Configuration;
using HSMClient.StatusHandlers;
using HSMClientWPFControls;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.UpdateObjects;
using HSMCommon.DataObjects;

namespace HSMClient.ConnectionNode
{
    class SensorMonitoringNode : OneConnectionMonitoringNode
    {
        public SensorMonitoringNode(string name, string address, SensorMonitoringInfo sensorInfo, MonitoringNodeBase parent = null) : base(name, address, parent)
        {
            Handler = new JobSensorsStatusHandler(sensorInfo);
        }

        public override MonitoringNodeUpdate ConvertResponse(string response)
        {
            MonitoringNodeUpdate result = new MonitoringNodeUpdate();
            response = response.Replace("[", "").Replace("]", "");
            ShortSensorData data = JsonSerializer.Deserialize<ShortSensorData>(response);
            MonitoringCounterUpdate update = new MonitoringCounterUpdate
            {
                ShortValue =  GetShortValue(data),
                DataObject = data,
                CounterType = CounterTypes.JobSensor
            };
            update.Name = this.Name;

            result.Counters = new List<MonitoringCounterUpdate> {update};
            result.Name = Parent.Name;
            result.SubNodes = new List<MonitoringNodeUpdate>();
            return result;
        }

        private string GetShortValue(ShortSensorData data)
        {
            if (string.IsNullOrEmpty(data.Comment))
            {
                return $"The task has been {ConvertStatus(data.Success)} at {data.Time:s}";
            }
            return $"The task has been {ConvertStatus(data.Success)} at {data.Time:s}   {data.Comment}";
        }

        private string ConvertStatus(bool status)
        {
            return status ? TextConstants.CompletedText : TextConstants.FailedText;
        }
    }
}
