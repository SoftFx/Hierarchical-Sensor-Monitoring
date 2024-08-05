using System;
using HSMServer.Core.Model;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
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
            
            values = base.ProcessingAndCompression(sensor, values, compressedValuesCount);

            var a = ProcessServiceAliveData(values);
            return new JsonResult(new
            {
               values = ProcessServiceAliveData(values).Select(x => (object)x)
            });
        }

        public List<ServiceAliveValue> ProcessServiceAliveData(List<BaseValue> values)
        {
            var xs = new Dictionary<DateTime, DateTime>();
            var response = new List<ServiceAliveValue>();

            DateTime startTime;
            DateTime endTime;
            
            for (var i = 0; i < values.Count; i++)
            {
                var value = values[i];

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
                
                var color = GetColor(values[i]);
                var customData = GetCustomData(values[i]) + startTime.ToDefaultFormat() + " - " + endTime.ToDefaultFormat();
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

            static string GetColor(BaseValue value)
            {
                if (value is BaseValue<bool> boolValue)
                {
                    switch (boolValue)
                    {
                        case {IsTimeout: true}:
                            return "#FF0000";
                        case {Value: false}:
                            return "#00FFFF";
                        default:
                            return "#94ff73";
                    }
                }

                return string.Empty;
            }
            
            static string GetCustomData(BaseValue value)
            {
                if (value is BaseValue<bool> boolValue)
                {
                    switch (boolValue)
                    {
                        case {IsTimeout: true}:
                            return "Timeout <br>";
                        case {Value: false}:
                            return "Restarting <br>";
                        default:
                            return "Running <br>";
                    }
                }

                return string.Empty;
            }
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