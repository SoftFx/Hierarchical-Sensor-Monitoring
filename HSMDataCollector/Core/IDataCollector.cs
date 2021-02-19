using System;
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
        public IDoubleBarSensor CreateDoubleBarSensor(string path, int timeout = 30000);
        public IIntBarSensor CreateIntBarSensor(string path, int timeout = 30000);
        public int GetSensorCount();

        event EventHandler ValuesQueueOverflow;
    }
}