using HSMDataCollector.Base;

namespace HSMDataCollector.Core
{
    public interface IDataCollector
    {
        //void Initialize();
        //void CheckConnection();
        ISensor CreateSensor(string path, SensorType type);
        
    }
}