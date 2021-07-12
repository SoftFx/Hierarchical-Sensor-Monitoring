using System;
using HSMDataCollector.PublicInterface;

namespace HSMDataCollector.Core
{
    public interface IDataCollector : IDisposable
    {
        #region Common methods

        /// <summary>
        /// The method sets the sending timer up. No data will be sent without calling this method
        /// </summary>
        void Initialize();
        /// <summary>
        /// Initializes monitoring, starts all monitoring timers and logger
        /// </summary>
        /// <param name="useLogging">Specifies whether runtime errors will be logged or not</param>
        /// <param name="folderPath">Path to logs folder, if null current folder will be used</param>
        /// <param name="fileNameFormat">File name format, if null default file name is specified</param>
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

        #endregion

        //07.07.2021: Use typed sensors
        //IBoolSensor CreateBoolSensor(string path);
        //IDoubleSensor CreateDoubleSensor(string path);
        //IIntSensor CreateIntSensor(string path);
        //IStringSensor CreateStringSensor(string path);
        //IDefaultValueSensorInt CreateDefaultValueSensorInt(string path, int defaultValue);
        //IDefaultValueSensorDouble CreateDefaultValueSensorDouble(string path, double defaultValue);

        IInstantValueSensor<bool> CreateBoolSensor(string path, string description = "");
        IInstantValueSensor<int> CreateIntSensor(string path, string description = "");
        IInstantValueSensor<double> CreateDoubleSensor(string path, string description = "");
        IInstantValueSensor<string> CreateStringSensor(string path, string description = "");
        ILastValueSensor<bool> CreateLastValueBoolSensor(string path, bool defaultValue, string description = "");
        ILastValueSensor<int> CreateLastValueIntSensor(string path, int defaultValue, string description = "");
        ILastValueSensor<double> CreateLastValueDoubleSensor(string path, double defaultValue, string description = "");
        ILastValueSensor<string> CreateLastValueStringSensor(string path, string defaultValue, string description = "");

        #region Bar sensors

        //08.07.2021. Use typed Bar sensors
        //IDoubleBarSensor CreateDoubleBarSensor(string path, int timeout = 300000, int smallPeriod = 15000, int precision = 2);
        //IDoubleBarSensor Create1HrDoubleBarSensor(string path, int precision = 2);
        //IDoubleBarSensor Create30MinDoubleBarSensor(string path, int precision = 2);
        //IDoubleBarSensor Create10MinDoubleBarSensor(string path, int precision = 2);
        //IDoubleBarSensor Create5MinDoubleBarSensor(string path, int precision = 2);
        //IDoubleBarSensor Create1MinDoubleBarSensor(string path, int precision = 2);
        //IIntBarSensor CreateIntBarSensor(string path, int timeout = 300000, int smallPeriod = 15000);
        //IIntBarSensor Create1HrIntBarSensor(string path);
        //IIntBarSensor Create30MinIntBarSensor(string path);
        //IIntBarSensor Create10MinIntBarSensor(string path);
        //IIntBarSensor Create5MinIntBarSensor(string path);
        //IIntBarSensor Create1MinIntBarSensor(string path);

        IBarSensor<int> CreateIntBarSensor(string path, int timeout = 300000, int smallPeriod = 15000, string description = "");
        IBarSensor<int> Create1HrIntBarSensor(string path, string description = "");
        IBarSensor<int> Create30MinIntBarSensor(string path, string description = "");
        IBarSensor<int> Create10MinIntBarSensor(string path, string description = "");
        IBarSensor<int> Create5MinIntBarSensor(string path, string description = "");
        IBarSensor<int> Create1MinIntBarSensor(string path, string description = "");
        IBarSensor<double> CreateDoubleBarSensor(string path, int timeout = 300000, int smallPeriod = 15000, int precision = 2, string description = "");
        IBarSensor<double> Create1HrDoubleBarSensor(string path, int precision = 2, string description = "");
        IBarSensor<double> Create30MinDoubleBarSensor(string path, int precision = 2, string description = "");
        IBarSensor<double> Create10MinDoubleBarSensor(string path, int precision = 2, string description = "");
        IBarSensor<double> Create5MinDoubleBarSensor(string path, int precision = 2, string description = "");
        IBarSensor<double> Create1MinDoubleBarSensor(string path, int precision = 2, string description = "");
        #endregion

        //int GetSensorCount();

        event EventHandler ValuesQueueOverflow;
    }
}