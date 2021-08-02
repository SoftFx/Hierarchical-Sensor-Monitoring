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

        public SensorDataViewModel(string path, SensorViewModel sensor)
        {
            var time = (DateTime.UtcNow - sensor.Time);
            UpdateTime = ListHelper.GetTimeAgo(time);
            Status = ViewHelper.GetStatusHeaderColorClass(sensor.Status);
            Id = $"{path.Replace(' ', '-')}_{sensor.Name.Replace(' ', '-')}";
            Value = sensor.ShortStringValue;
        }
    }
}
