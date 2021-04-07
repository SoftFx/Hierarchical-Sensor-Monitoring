using HSMSensorDataObjects;

namespace HSMDataCollector.Base
{
    internal interface ISensor
    {
        bool HasLastValue { get; }
        CommonSensorValue GetLastValue();
        void Dispose();
    }
}