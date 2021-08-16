using HSMCommon.Model.SensorsData;
using System;
using System.Collections.Generic;

namespace HSMServer.MonitoringHistoryProcessor.Processor
{
    internal class DoubleHistoryProcessor : HistoryProcessorBase
    {
        public DoubleHistoryProcessor(TimeSpan periodInterval) : base(periodInterval)
        {

        }
        public override List<SensorHistoryData> ProcessHistory(List<SensorHistoryData> uncompressedData)
        {
            throw new System.NotImplementedException();
        }
    }
}