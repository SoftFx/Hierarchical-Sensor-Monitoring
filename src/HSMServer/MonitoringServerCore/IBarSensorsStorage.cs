﻿using System;
using System.Collections.Generic;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Model;

namespace HSMServer.MonitoringServerCore
{
    public interface IBarSensorsStorage : IDisposable
    {
        void Add(IntBarSensorValue value, string product, DateTime timeCollected);
        void Add(DoubleBarSensorValue value, string product, DateTime timeCollected);
        void Remove(string product, string path);
        ExtendedBarSensorData GetLastValue(string product, string path);
        List<ExtendedBarSensorData> GetAllLastValues();
        event EventHandler<ExtendedBarSensorData> IncompleteBarOutdated;
    }
}