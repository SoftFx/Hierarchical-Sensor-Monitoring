using System.Collections.Generic;
using HSMCommon.Model.SensorsData;

namespace HSMServer.MonitoringHistoryProcessor.Processor
{
    internal interface IHistoryProcessor
    {
        List<SensorHistoryData> ProcessHistory(List<SensorHistoryData> uncompressedData);
        string GetCsvHistory(List<SensorHistoryData> originalData);
    }
}