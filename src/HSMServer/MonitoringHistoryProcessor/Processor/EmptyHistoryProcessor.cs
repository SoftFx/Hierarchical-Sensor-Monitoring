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
        
        public override string GetCsvHistory(List<SensorHistoryData> originalData)
        {
            return string.Empty;
        }
    }
}
