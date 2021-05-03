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
        void Initialize(bool useLogging = true, string folderPath = null, string fileNameFormat = null);
        /// <summary>
        /// This method must be called before stopping the application. It sends all the data left, stops and disposes the timer.
        /// The method also disposes the HttpClient.
        /// </summary>
        void Stop();

        /// <summary>
        /// Creates and initializes sensors, which automatically monitor CPU and RAM usage of the current machine.
        /// Sensors will be placed at Product/System Monitoring node
        /// </summary>
        /// <param name="isCPU">Specifies whether the sensor for current CPU usage is created</param>
        /// <param name="isFreeRam">Specifies whether the sensor for current free RAM in mb is created</param>
        void InitializeSystemMonitoring(bool isCPU, bool isFreeRam);
        /// <summary>
        /// Creates and initializes sensors, which automatically monitor current working process. RAM and CPU usage, and threads amount are monitored.
        /// Sensors will be placed at Product/System Monitoring node
        /// </summary>
        /// <param name="isCPU">Specifies whether the sensor for current process CPU is created</param>
        /// <param name="isMemory">Specifies whether the sensor for current process RAM (in mb) is created</param>
        /// <param name="isThreads">Specifies whether the sensor for current process thread count is created</param>
        void InitializeProcessMonitoring(bool isCPU, bool isMemory, bool isThreads);
        /// <summary>
        /// Creates and initializes sensors, which automatically monitor the specified process. RAM and CPU usage, and threads amount are monitored.
        /// Sensors will be placed at Product/System Monitoring node
        /// </summary>
        /// <param name="processName">Specifies the name of the process</param>
        /// <param name="isCPU">Specifies whether the sensor for the specified process CPU is created</param>
        /// <param name="isMemory">Specifies whether the sensor for the specified process RAM (in mb) is created</param>
        /// <param name="isThreads">Specifies whether the sensor for the specified process thread count is created</param>
        void InitializeProcessMonitoring(string processName, bool isCPU, bool isMemory, bool isThreads);
        /// <summary>
        /// Creates a sensor, which sends boolean value true every 15 seconds to indicate whether the service is alive
        /// </summary>
        void MonitorServiceAlive();
        IBoolSensor CreateBoolSensor(string path);
        IDoubleSensor CreateDoubleSensor(string path);
        IIntSensor CreateIntSensor(string path);
        IStringSensor CreateStringSensor(string path);
        IDefaultValueSensorInt CreateDefaultValueSensorInt(string path, int defaultValue);
        IDefaultValueSensorDouble CreateDefaultValueSensorDouble(string path, double defaultValue);

        #region Bar sensors

        IDoubleBarSensor CreateDoubleBarSensor(string path, int timeout = 300000, int smallPeriod = 15000, int precision = 2);
        IDoubleBarSensor Create1HrDoubleBarSensor(string path, int precision = 2);
        IDoubleBarSensor Create30MinDoubleBarSensor(string path, int precision = 2);
        IDoubleBarSensor Create10MinDoubleBarSensor(string path, int precision = 2);
        IDoubleBarSensor Create5MinDoubleBarSensor(string path, int precision = 2);
        IDoubleBarSensor Create1MinDoubleBarSensor(string path, int precision = 2);
        IIntBarSensor CreateIntBarSensor(string path, int timeout = 300000, int smallPeriod = 15000);
        IIntBarSensor Create1HrIntBarSensor(string path);
        IIntBarSensor Create30MinIntBarSensor(string path);
        IIntBarSensor Create10MinIntBarSensor(string path);
        IIntBarSensor Create5MinIntBarSensor(string path);
        IIntBarSensor Create1MinIntBarSensor(string path);

        #endregion

        //int GetSensorCount();

        event EventHandler ValuesQueueOverflow;
    }
}