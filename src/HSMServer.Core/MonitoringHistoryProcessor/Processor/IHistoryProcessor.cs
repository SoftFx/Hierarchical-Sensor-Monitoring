using HSMServer.Core.Model.Sensor;
using System.Collections.Generic;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    public interface IHistoryProcessor
    {
        List<SensorHistoryData> ProcessHistory(List<SensorHistoryData> uncompressedData);
        string GetCsvHistory(List<SensorHistoryData> originalData);
    }
}