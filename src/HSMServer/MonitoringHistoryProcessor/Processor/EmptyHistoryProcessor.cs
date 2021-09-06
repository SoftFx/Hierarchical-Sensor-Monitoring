using System;
using System.Collections.Generic;
using HSMCommon.Model.SensorsData;

namespace HSMServer.MonitoringHistoryProcessor.Processor
{
    internal class EmptyHistoryProcessor : HistoryProcessorBase
    {
        public EmptyHistoryProcessor(TimeSpan periodInterval) : base(periodInterval)
        {
        }

        public override List<SensorHistoryData> ProcessHistory(List<SensorHistoryData> uncompressedData)
        {
            uncompressedData.Sort((d1, d2) => d1.Time.CompareTo(d2.Time));
            return uncompressedData;
        }
    }
}
