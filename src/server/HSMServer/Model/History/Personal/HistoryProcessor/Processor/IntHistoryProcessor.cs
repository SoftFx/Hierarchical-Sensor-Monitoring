using System;
using HSMServer.Core.Model;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HSMServer.Core.Extensions;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Model.History
{
    internal sealed class IntHistoryProcessor : HistoryProcessorBase
    {
        public override JsonResult GetResultFromValues(SensorNodeViewModel sensor, List<BaseValue> values, int compressedValuesCount)
        {
            if (!sensor.IsServiceStatus)
                return base.GetResultFromValues(sensor, values, compressedValuesCount);
            
            values = base.ProcessingAndCompression(sensor, values, compressedValuesCount);

            return new JsonResult(new
            {
                values = ProcessServiceAliveData(values).Select(x => (object)x)
            });
        }
        
        public List<ServiceAliveValue> ProcessServiceAliveData(List<BaseValue> values)
        {
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
                
                var (color, customData) = GetColorAndCustomData(values[i]);
                response.Add(new ServiceAliveValue()
                {
                    X0 = startTime,
                    X1 = endTime,
                    X = new DateTime(startTime.Ticks/2 + endTime.Ticks/2),
                    Color = color,
                    CustomData = customData + "<br>" + startTime.ToDefaultFormat() + " - " + endTime.ToDefaultFormat()
                });
            }

            return response;

            static (string color, string customdata) GetColorAndCustomData(BaseValue value)
            {
                if (value is BaseValue<int> intValue)
                {
                    switch (intValue.Value)
                    {
                       case 1:
                           return ("#FF0000", "Stopped");
                       case 2:
                           return ("#BFFFBF", "Start Pending");
                       case 3:
                           return ("#FD6464", "Stop Pending");
                       case 4:
                           return ("#00FF00", "Running");
                       case 5:
                           return ("#FFB403", "Continue Pending");
                       case 6:
                           return ("#809EFF", "Pause Pending");
                       case 7:
                           return ("#0314FF", "Paused");
                       case 8:
                           return ("#666699", "Timeout");
                       case 0:
                           return ("#000000", "Unknown");
                    }
                }
                return ("#000000", "Unknown");
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
}
