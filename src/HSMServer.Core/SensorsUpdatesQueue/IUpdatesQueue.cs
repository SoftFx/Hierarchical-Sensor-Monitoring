using HSMSensorDataObjects.FullDataObject;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.SensorsUpdatesQueue
{
    public interface IUpdatesQueue : IDisposable
    {
        event Action<List<SensorValueBase>> NewItemsEvent;

        void AddItem(SensorValueBase sensorValue);
        void AddItems(IEnumerable<SensorValueBase> sensorValues);
    }
}
