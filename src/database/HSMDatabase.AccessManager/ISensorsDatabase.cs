using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager
{
    public interface ISensorsDatabase : IDisposable
    {
        long DatabaseMinTicks { get; }
        long DatabaseMaxTicks { get; }
        DateTime DatabaseMaxDateTime { get; }
        DateTime DatabaseMinDateTime { get; }
        long GetSensorSize(string productName, string path);
        void DeleteAllSensorValues(string productName, string path);
        List<byte[]> GetSensorValues(string productName, string path, DateTime to, int count);
        List<byte[]> GetSensorValuesBytesBetweenTo(string productName, string path, DateTime from, DateTime to, int count);
        List<byte[]> GetSensorValuesBytesBetweenFrom(string productName, string path, DateTime from, DateTime to, int count);

        void FillLatestValues(Dictionary<byte[], (Guid sensorId, byte[] latestValue)> keyValuePairs);
    }
}
