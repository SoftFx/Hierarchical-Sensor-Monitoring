using System;
using HSMServer.Core.Model;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Model.History
{
    internal sealed class BoolHistoryProcessor : HistoryProcessorBase
    {
        public override JsonResult GetResultFromValues(SensorNodeViewModel sensor, List<BaseValue> values, int compressedValuesCount)
        {
            if (!sensor.IsServiceAlive)
                return base.GetResultFromValues(sensor, values, compressedValuesCount);
            
            values = ProcessingAndCompression(sensor, values, compressedValuesCount);

            return new JsonResult(new
            {
               values = ProcessServiceAliveData(values, GetColorAndCustomData).Select(x => (object)x)
            });
        }

        public static List<ServiceAliveValue> ProcessServiceAliveData(List<BaseValue> values, Func<BaseValue, DateTime, DateTime, (string, string)> getColorAndCustomData)
        {
            var response = new List<ServiceAliveValue>();

            for (var i = 0; i < values.Count; i++)
            {
                var value = values[i];

                DateTime startTime;
                DateTime endTime;
                if (value.IsTimeout)
                {
                    startTime = value.LastUpdateTime;
                    if (i + 1 < values.Count)
                    {
                        endTime = values[i + 1].Time;
                    }
                    else endTime = DateTime.UtcNow;
                }
                else
                {
                    startTime = value.Time;
                    endTime = value.LastUpdateTime;
                }
                
                var (color, customData) = getColorAndCustomData(values[i], startTime, endTime);
                response.Add(new ServiceAliveValue()
                {
                    X0 = startTime,
                    X1 = endTime,
                    X = new DateTime(startTime.Ticks/2 + endTime.Ticks/2),
                    Color = color,
                    CustomData = customData
                });
            }

            return response;
        }
        
        static (string color, string customData) GetColorAndCustomData(BaseValue value, DateTime startTime, DateTime endTime)
        {
            if (value is BaseValue<bool> boolValue)
            {
                switch (boolValue)
                {
                    case {IsTimeout: true}:
                        return ("#FF0000", $"Timeout <br> {startTime.ToDefaultFormat()} - {endTime.ToDefaultFormat()}");
                    case {Value: false}:
                        return ("#00FFFF", $"Restarting <br> {startTime.ToDefaultFormat()} - {endTime.ToDefaultFormat()}");
                    default:
                        return ("#94ff73", $"Running <br> {startTime.ToDefaultFormat()} - {endTime.ToDefaultFormat()}");
                }
            }

            return (string.Empty, string.Empty);
        }
    }

    public record ServiceAliveValue
    {
        public DateTime? X0 { get; set; }
        public DateTime? X1 { get; set; }
        
        public DateTime? X { get; set; }
        
        public string Color { get; set; }
        
        public string CustomData { get; set; }
    }
}