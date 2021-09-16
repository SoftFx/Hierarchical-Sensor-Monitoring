using System.Collections.Generic;
using HSMCommon.Model.SensorsData;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    public interface IHistoryProcessor
    {
        List<SensorHistoryData> ProcessHistory(List<SensorHistoryData> uncompressedData);
        string GetCsvHistory(List<SensorHistoryData> originalData);
    }
}