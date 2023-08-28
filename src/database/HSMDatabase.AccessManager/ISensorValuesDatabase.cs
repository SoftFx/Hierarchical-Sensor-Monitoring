﻿using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager
{
    public interface ISensorValuesDatabase : IDisposable
    {
        string Name { get; }

        long From { get; }

        long To { get; }


        bool IsInclude(long time);

        bool IsInclude(long from, long to);

        void FillLatestValues(Dictionary<byte[], (long from, byte[] toKey, byte[] latestValue)> keyValuePairs);

        void PutSensorValue(byte[] key, object value);

        void RemoveSensorValues(byte[] from, byte[] to);

        byte[] Get(byte[] key, byte[] sensorId);

        byte[] GetLatest(byte[] to, byte[] sensorId);

        IEnumerable<byte[]> GetValuesFrom(byte[] from, byte[] to);

        IEnumerable<byte[]> GetValuesTo(byte[] from, byte[] to);
    }
}
