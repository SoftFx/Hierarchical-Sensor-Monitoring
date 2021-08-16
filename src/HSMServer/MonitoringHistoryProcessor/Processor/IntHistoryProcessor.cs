using HSMCommon.Model.SensorsData;
using System;
using System.Collections.Generic;
using System.Text.Json;
using HSMSensorDataObjects.TypedDataObject;

namespace HSMServer.MonitoringHistoryProcessor.Processor
{
    internal class IntHistoryProcessor : HistoryProcessorBase
    {
        public IntHistoryProcessor(TimeSpan periodInterval) : base(periodInterval)
        {
        }

        public override List<SensorHistoryData> ProcessHistory(List<SensorHistoryData> uncompressedData)
        {
            //uncompressedData.Sort((d1, d2) => d2.Time.CompareTo(d1.Time));
            //List<SensorHistoryData> result = new List<SensorHistoryData>();
            //IntSensorData currentItem = new IntSensorData();
            //foreach (var dataItem in uncompressedData)
            //{
            //    IntSensorData data = JsonSerializer.Deserialize<IntSensorData>(dataItem.TypedData);

            //}
            throw new NotImplementedException();
        }
    }
}