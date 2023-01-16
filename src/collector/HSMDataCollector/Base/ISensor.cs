using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.Base
{
    internal interface ISensor
    {
        bool HasLastValue { get; }


        void Dispose();

        SensorValueBase GetLastValue();
    }
}