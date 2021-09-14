using HSMCommon.Model.SensorsData;
using HSMSensorDataObjects.TypedDataObject;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    internal class DoubleHistoryProcessor : HistoryProcessorBase
    {
        private readonly NumberFormatInfo _format;
        public DoubleHistoryProcessor(TimeSpan periodInterval) : base(periodInterval)
        {
            _format = new NumberFormatInfo();
            _format.NumberDecimalSeparator = ".";
        }

        public override string GetCsvHistory(List<SensorHistoryData> originalData)
        {
            List<DoubleSensorData> typedDatas = GetTypeDatas(originalData);
            StringBuilder sb = new StringBuilder();
            sb.Append($"Index,Time,Value,Comment{Environment.NewLine}");
            for (int i = 0; i < typedDatas.Count; ++i)
            {
                sb.Append(
                    $"{i},{originalData[i].Time.ToUniversalTime():s},{typedDatas[i].DoubleValue.ToString(_format)}" +
                    $",{typedDatas[i].Comment}{Environment.NewLine}");
            }

            return sb.ToString();
        }

        private List<DoubleSensorData> GetTypeDatas(List<SensorHistoryData> uncompressedData)
        {
            List<DoubleSensorData> result = new List<DoubleSensorData>();
            uncompressedData.Sort((d1, d2) => d1.Time.CompareTo(d2.Time));
            foreach (var unProcessed in uncompressedData)
            {
                try
                {
                    result.Add(JsonSerializer.Deserialize<DoubleSensorData>(unProcessed.TypedData));
                }
                catch (Exception e)
                { }
            }

            return result;
        }
    }
}
