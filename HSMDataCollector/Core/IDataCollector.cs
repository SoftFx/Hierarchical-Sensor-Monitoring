using System;
using HSMDataCollector.PublicInterface;

namespace HSMDataCollector.Core
{
    public interface IDataCollector : IDisposable
    {
        /// <summary>
        /// The method sets the sending timer up. No data will be sent without calling this method
        /// </summary>
        void Initialize();
        /// <summary>
        /// This method must be called before stopping the application. It sends all the data left, stops and disposes the timer.
        /// The method also disposes the HttpClient.
        /// </summary>
        void Stop();
        public IBoolSensor CreateBoolSensor(string path);
        public IDoubleSensor CreateDoubleSensor(string path);
        public IIntSensor CreateIntSensor(string path);
        public IStringSensor CreateStringSensor(string path);

        #region Bar sensors

        public IDoubleBarSensor CreateDoubleBarSensor(string path, int timeout = 300000, int smallPeriod = 15000);
        public IDoubleBarSensor Create1HrDoubleBarSensor(string path);
        public IDoubleBarSensor Create30MinDoubleBarSensor(string path);
        public IDoubleBarSensor Create10MinDoubleBarSensor(string path);
        public IDoubleBarSensor Create5MinDoubleBarSensor(string path);
        public IDoubleBarSensor Create1MinDoubleBarSensor(string path);
        public IIntBarSensor CreateIntBarSensor(string path, int timeout = 300000, int smallPeriod = 15000);
        public IIntBarSensor Create1HrIntBarSensor(string path);
        public IIntBarSensor Create30MinIntBarSensor(string path);
        public IIntBarSensor Create10MinIntBarSensor(string path);
        public IIntBarSensor Create5MinIntBarSensor(string path);
        public IIntBarSensor Create1MinIntBarSensor(string path);

        #endregion

        public int GetSensorCount();

        event EventHandler ValuesQueueOverflow;
    }
}