using HSMServer.Core.Model.Sensor;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    internal class EmptyHistoryProcessor : HistoryProcessorBase
    {
        public EmptyHistoryProcessor()
        {

        }
        public EmptyHistoryProcessor(TimeSpan periodInterval) : base(periodInterval)
        {
        }

        public override string GetCsvHistory(List<SensorHistoryData> originalData)
        {
            return string.Empty;
        }
    }
}
