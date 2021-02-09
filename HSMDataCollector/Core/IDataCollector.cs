using HSMDataCollector.Base;
using HSMDataCollector.PublicInterface;

namespace HSMDataCollector.Core
{
    public interface IDataCollector
    {
        //void Initialize();
        //void CheckConnection();

        public IBoolSensor CreateBoolSensor(string path);
        public IDoubleSensor CreateDoubleSensor(string path);
        public IIntSensor CreateIntSensor(string path);
        public IDoubleBarSensor CreateDoubleBarSensor(string path);
        public IIntBarSensor CreateIntBarSensor(string path);
        public int GetSensorCount();
    }
}