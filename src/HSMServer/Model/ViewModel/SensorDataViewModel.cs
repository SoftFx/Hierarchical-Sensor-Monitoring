using HSMServer.Helpers;
using HSMServer.HtmlHelpers;
using System;

namespace HSMServer.Model.ViewModel
{
    public class SensorDataViewModel
    {
        public string Id { get; set; }
        public string Status { get; set; }
        public string Value { get; set; }
        public string UpdateTime { get; set; }
        public string ValidationError { get; set; }

        public SensorDataViewModel(string path, SensorViewModel sensor)
        {
            var time = (DateTime.UtcNow - sensor.Time);
            UpdateTime = ListHelper.GetTimeAgo(time);
            Status = ViewHelper.GetStatusHeaderColorClass(sensor.Status);

            int index = path.IndexOf('_');
            var encodedPath = path.Substring(index + 1, path.Length - index - 1);
            var decodedPath = SensorPathHelper.Decode(encodedPath);

            Id = "sensor_" + SensorPathHelper.Encode($"{decodedPath}/{sensor.Name}");
            
            Value = sensor.ShortStringValue;
            ValidationError = sensor.ValidationError;
        }
    }
}
