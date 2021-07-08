using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;

namespace HSMDataCollector.Base
{
    internal interface ISensor
    {
        bool HasLastValue { get; }
        CommonSensorValue GetLastValue();
        SensorValueBase GetLastValueNew();
        void Dispose();
    }
}