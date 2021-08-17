using HSMCommon.Model.SensorsData;
using System;
using System.Collections.Generic;

namespace HSMServer.MonitoringHistoryProcessor.Processor
{
    internal class DoubleBarHistoryProcessor : HistoryProcessorBase
    {
        public DoubleBarHistoryProcessor(TimeSpan periodInterval) : base(periodInterval)
        {
        }

        public override List<SensorHistoryData> ProcessHistory(List<SensorHistoryData> uncompressedData)
        {
            return uncompressedData;
        }
    }
}
