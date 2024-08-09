using System;
using HSMServer.Core.Model;
using System.Collections.Generic;
using System.Linq;
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
            
            values = ProcessingAndCompression(sensor, values, compressedValuesCount);

            return new JsonResult(new
            {
                values = BoolHistoryProcessor.ProcessServiceAliveData(values, GetColorAndCustomData).Select(x => (object)x)
            });
        }
        
        static (string color, string customdata) GetColorAndCustomData(BaseValue value, DateTime startTime, DateTime endTime)
        {
            if (value is BaseValue<int> intValue)
            {
                switch (intValue.Value)
                {
                    case 1:
                        return ("#FF0000", $"Stopped <br> {startTime.ToDefaultFormat()}  - {endTime.ToDefaultFormat()}");
                    case 2:
                        return ("#BFFFBF", $"Start Pending <br> {startTime.ToDefaultFormat()}  - {endTime.ToDefaultFormat()}");
                    case 3:
                        return ("#FD6464", $"Stop Pending <br> {startTime.ToDefaultFormat()}  - {endTime.ToDefaultFormat()}");
                    case 4:
                        return ("#00FF00", $"Running <br> {startTime.ToDefaultFormat()}  - {endTime.ToDefaultFormat()}");
                    case 5:
                        return ("#FFB403", $"Continue Pending <br> {startTime.ToDefaultFormat()}  - {endTime.ToDefaultFormat()}");
                    case 6:
                        return ("#809EFF", $"Pause Pending <br> {startTime.ToDefaultFormat()}  - {endTime.ToDefaultFormat()}");
                    case 7:
                        return ("#0314FF", $"Paused <br> {startTime.ToDefaultFormat()}  - {endTime.ToDefaultFormat()}");
                    case 8:
                        return ("#666699", "Timeout");
                    case 0:
                        return ("#000000", $"Unknown <br> {startTime.ToDefaultFormat()}  - {endTime.ToDefaultFormat()}");
                }
            }
            return ("#000000", $"Unknown <br> {startTime.ToDefaultFormat()}  - {endTime.ToDefaultFormat()}");
        }
    }
}
