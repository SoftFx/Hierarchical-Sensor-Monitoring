using HSMSensorDataObjects.FullDataObject;

namespace HSMDataCollector.Base
{
    internal interface ISensor
    {
        bool HasLastValue { get; }
        SensorValueBase GetLastValue();
        void Dispose();
    }
}