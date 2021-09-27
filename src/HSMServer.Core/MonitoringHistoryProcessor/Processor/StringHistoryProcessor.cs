using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Model.Sensor;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    internal class StringHistoryProcessor : HistoryProcessorBase
    {
        public StringHistoryProcessor(TimeSpan periodInterval) : base(periodInterval)
        {
        }

        public override string GetCsvHistory(List<SensorHistoryData> originalData)
        {
            List<StringSensorData> typedDatas = GetTypeDatas(originalData);
            StringBuilder sb = new StringBuilder();
            sb.Append($"Index,Time,Value,Comment{Environment.NewLine}");
            for (int i = 0; i < typedDatas.Count; ++i)
            {
                sb.Append(
                    $"{i},{originalData[i].Time.ToUniversalTime():s},{typedDatas[i].StringValue},{typedDatas[i].Comment}{Environment.NewLine}");
            }

            return sb.ToString();
        }

        private List<StringSensorData> GetTypeDatas(List<SensorHistoryData> uncompressedData)
        {
            List<StringSensorData> result = new List<StringSensorData>();
            uncompressedData.Sort((d1, d2) => d1.Time.CompareTo(d2.Time));
            foreach (var unProcessed in uncompressedData)
            {
                try
                {
                    result.Add(JsonSerializer.Deserialize<StringSensorData>(unProcessed.TypedData));
                }
                catch (Exception e)
                { }
            }

            return result;
        }
    }
}
