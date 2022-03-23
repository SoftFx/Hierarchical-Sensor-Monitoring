using HSMSensorDataObjects.FullDataObject;
using System;

namespace HSMServer.Core.SensorsUpdatesQueue
{
    public interface IUpdatesQueue
    {
        event Action<SensorValueBase> NewItemEvent;

        void AddItem(SensorValueBase sensorValue);
        void Stop();
    }
}
