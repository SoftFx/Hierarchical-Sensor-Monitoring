using HSMCommon.Model.SensorsData;
using HSMSensorDataObjects.TypedDataObject;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    internal class BoolHistoryProcessor : HistoryProcessorBase
    {
        public BoolHistoryProcessor(TimeSpan periodInterval) : base(periodInterval)
        {
        }

        public override string GetCsvHistory(List<SensorHistoryData> originalData)
        {
            List<BoolSensorData> typedDatas = GetTypeDatas(originalData);
            StringBuilder sb = new StringBuilder();
            sb.Append($"Index,Time,Value,Comment{Environment.NewLine}");
            for (int i = 0; i < typedDatas.Count; ++i)
            {
                sb.Append(
                    $"{i},{originalData[i].Time.ToUniversalTime():s},{typedDatas[i].BoolValue},{typedDatas[i].Comment}{Environment.NewLine}");
            }

            return sb.ToString();
        }

        private List<BoolSensorData> GetTypeDatas(List<SensorHistoryData> uncompressedData)
        {
            List<BoolSensorData> result = new List<BoolSensorData>();
            uncompressedData.Sort((d1, d2) => d1.Time.CompareTo(d2.Time));
            foreach (var unProcessed in uncompressedData)
            {
                try
                {
                    result.Add(JsonSerializer.Deserialize<BoolSensorData>(unProcessed.TypedData));
                }
                catch (Exception e)
                { }
            }

            return result;
        }
    }
}