using HSMSensorDataObjects.FullDataObject;

namespace HSMDataCollector.Base
{
    internal interface ISensor
    {
        bool HasLastValue { get; }
        UnitedSensorValue GetLastValue();
        void Dispose();
    }
}