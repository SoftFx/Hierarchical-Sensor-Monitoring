using HSMDataCollector.DefaultSensors;
using HSMServer.Core.Model;
using HSMServer.Core.ApiObjectsConverters;
using HSMServer.Core.Services;


namespace HSMServer.ApiObjectsConverters
{
    public static class ApiConverters
    {
        public static IntegerBarValue Convert(this IntMonitoringBar value) =>
            new()
            {
                Comment = HtmlSanitizerService.Sanitize(value.Comment),
                Time = value.Time,
                Status = value.Status.Convert(),
                Count = value.Count,
                OpenTime = value.OpenTime,
                CloseTime = value.CloseTime,
                Min = value.Min,
                Max = value.Max,
                Mean = value.Mean,
                FirstValue = value.FirstValue,
                LastValue = value.LastValue,
            };


        public static DoubleBarValue Convert(this DoubleMonitoringBar value) =>
            new()
            {
                Comment = HtmlSanitizerService.Sanitize(value.Comment),
                Time = value.Time,
                Status = value.Status.Convert(),
                Count = value.Count,
                OpenTime = value.OpenTime,
                CloseTime = value.CloseTime,
                Min = value.Min,
                Max = value.Max,
                Mean = value.Mean,
                FirstValue = value.FirstValue,
                LastValue = value.LastValue,
            };
    }

}