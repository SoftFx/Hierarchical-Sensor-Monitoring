using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Model.Sensor;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    internal class IntHistoryProcessor : HistoryProcessorBase
    {
        public IntHistoryProcessor(TimeSpan periodInterval) : base(periodInterval)
        {
        }

        public override string GetCsvHistory(List<SensorHistoryData> originalData)
        {
            List<IntSensorData> typedDatas = GetTypeDatas(originalData);
            StringBuilder sb = new StringBuilder();
            sb.Append($"Index,Time,Value,Comment{Environment.NewLine}");
            for (int i = 0; i < typedDatas.Count; ++i)
            {
                sb.Append(
                    $"{i},{originalData[i].Time.ToUniversalTime():s},{typedDatas[i].IntValue},{typedDatas[i].Comment}{Environment.NewLine}");
            }

            return sb.ToString();
        }

        private List<IntSensorData> GetTypeDatas(List<SensorHistoryData> uncompressedData)
        {
            List<IntSensorData> result = new List<IntSensorData>();
            uncompressedData.Sort((d1, d2) => d1.Time.CompareTo(d2.Time));
            foreach (var unProcessed in uncompressedData)
            {
                try
                {
                    result.Add(JsonSerializer.Deserialize<IntSensorData>(unProcessed.TypedData));
                }
                catch (Exception e)
                { }
            }

            return result;
        }
    }
}
