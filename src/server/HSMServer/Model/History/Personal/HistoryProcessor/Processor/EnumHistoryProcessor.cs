using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using HSMServer.Core.Extensions;
using HSMServer.Model.TreeViewModel;
using HSMServer.Core.Model.Sensors;
using ColorExtensions = HSMServer.Extensions.ColorExtensions;
using HSMCommon.Model;


namespace HSMServer.Model.History
{

    internal sealed class EnumHistoryProcessor : HistoryProcessorBase
    {
        public override JsonResult GetResultFromValues(SensorNodeViewModel sensor, List<BaseValue> values, int compressedValuesCount)
        {
            values = ProcessingAndCompression(sensor, values, compressedValuesCount);

            return new JsonResult(new
            {
                values = BoolHistoryProcessor.ProcessServiceAliveData(sensor, values, GetColorAndCustomData).Select(x => (object)x)
            });
        }
        
        private (string color, string customdata) GetColorAndCustomData(SensorNodeViewModel sensor, BaseValue value, DateTime startTime, DateTime endTime)
        {
            if (value is EnumValue enumValue)
            {
                if (sensor.EnumOptions?.Count > 0)
                {
                    if (sensor.EnumOptions.TryGetValue(enumValue.Value, out EnumOptionModel enumOptionsModel))
                        return (ColorExtensions.ArgbToHtml(enumOptionsModel.Color), $"{enumOptionsModel.Value} {enumOptionsModel.Description} <br> {startTime.ToDefaultFormat()}  - {endTime.ToDefaultFormat()}");
                }
                else
                {
                    return (ColorExtensions.GetDefaultColor(enumValue.Value), $"{enumValue.Value} <br> {startTime.ToDefaultFormat()}  - {endTime.ToDefaultFormat()}");
                }
            }

            return ("#000000", $"Unknown <br> {startTime.ToDefaultFormat()}  - {endTime.ToDefaultFormat()}");
        }
    }
}
