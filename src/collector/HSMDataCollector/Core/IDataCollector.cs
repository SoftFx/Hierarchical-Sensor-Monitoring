using HSMDataCollector.Logging;
using HSMDataCollector.PublicInterface;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HSMSensorDataObjects;

namespace HSMDataCollector.Core
{
    public interface IDataCollector : IDisposable
    {
        IWindowsCollection Windows { get; }

        IUnixCollection Unix { get; }

        Task Start();

        IDataCollector AddNLog(LoggerOptions options = null);

        #region Common methods

        /// <summary>
        /// The method sets the sending timer up. No data will be sent without calling this method
        /// </summary>
        [Obsolete("Use Initialize(bool, string, string)")]
        void Initialize();

        /// <summary>
        /// Initializes monitoring, starts all monitoring timers and logger
        /// </summary>
        /// <param name="useLogging">Specifies whether runtime errors will be logged or not</param>
        /// <param name="folderPath">Path to logs folder, if null current folder will be used</param>
        /// <param name="fileNameFormat">File name format, if null default file name is specified</param>
        [Obsolete("Use method AddNLog() to add logging and method Start() after default sensors initialization")]
        void Initialize(bool useLogging = true, string folderPath = null, string fileNameFormat = null);

        /// <summary>
        /// This method must be called before stopping the application. It sends all the data left, stops and disposes the timer.
        /// The method also disposes the HttpClient.
        /// </summary>
        void Stop();

        /// <summary>
        /// Creates and initializes sensors, which automatically monitor CPU and RAM usage of the current machine.
        /// Sensors will be placed at Product/System Monitoring node or at Product/specificPath node
        /// Please note, that system monitoring is currently unavailable for non-windows operating systems (e.g. Linux)
        /// </summary>
        /// <param name="isCPU">Specifies whether the sensor for current CPU usage is created</param>
        /// <param name="isFreeRam">Specifies whether the sensor for current free RAM in mb is created</param>
        /// <param name="specificPath">Specifies where sensors are created</param>
        [Obsolete("Use method AddSystemMonitoringSensors(options) in Windows collection")]
        void InitializeSystemMonitoring(bool isCPU, bool isFreeRam, string specificPath = null);

        /// <summary>
        /// Creates and initializes sensors, which automatically monitor current working process. RAM and CPU usage, and threads amount are monitored.
        /// Sensors will be placed at Product/CurrentProcess node or at Product/specificPath node
        /// </summary>
        /// <param name="isCPU">Specifies whether the sensor for current process CPU is created</param>
        /// <param name="isMemory">Specifies whether the sensor for current process RAM (in mb) is created</param>
        /// <param name="isThreads">Specifies whether the sensor for current process thread count is created</param>
        /// <param name="specificPath">Specifies where sensors are created</param>
        [Obsolete("Use method AddProcessSensors(options) in Windows or Unix collections")]
        void InitializeProcessMonitoring(bool isCPU, bool isMemory, bool isThreads, string specificPath = null);

        /// <summary>
        /// Creates and initializes sensors, which automatically monitor the specified process. RAM and CPU usage, and threads amount are monitored.
        /// Sensors will be placed at Product/System Monitoring node
        /// </summary>
        /// <param name="processName">Specifies the name of the process</param>
        /// <param name="isCPU">Specifies whether the sensor for the specified process CPU is created</param>
        /// <param name="isMemory">Specifies whether the sensor for the specified process RAM (in mb) is created</param>
        /// <param name="isThreads">Specifies whether the sensor for the specified process thread count is created</param>
        /// <param name="specificPath">Specifies where sensors are created</param>
        [Obsolete("Method has no implementation")]
        void InitializeProcessMonitoring(string processName, bool isCPU, bool isMemory, bool isThreads, string specificPath = null);

        /// <summary>
        /// Creates and initializes sensors, which automatically monitor operational system properties. Update status is monitored.
        /// Sensors will be placed at Product/System Monitoring node or at Product/specificPath node
        /// </summary>
        /// <param name="isUpdated">Specifies whether the sensor for the OS update status is created</param>
        /// <param name="specificPath">Specifies where sensors are created</param>
        [Obsolete("Use method AddWindowsSensors(options) in Windows collection")]
        void InitializeOsMonitoring(bool isUpdated, string specificPath = null);

        /// <summary>
        /// Creates a sensor, which sends boolean value true every 15 seconds to indicate whether the service is alive
        /// </summary>
        /// <param name="specificPath">Specifies where sensors are created</param>
        [Obsolete("Use method AddCollectorAlive(options) in Windows or Unix collections")]
        void MonitorServiceAlive(string specificPath = null);

        /// <summary>
        ///  Creates a sensor, which sends boolean value when since windows update date passed more time then <see cref="updateInterval"/>
        /// </summary>
        /// <param name="sensorInterval">The function is invoked every time the interval passes</param>
        /// <param name="updateInterval">Time interval for the version to become old</param>
        /// <param name="specificPath">Specifies where sensors are created</param>
        [Obsolete("Use method AddWindowsSensors(options) in Windows collection")]
        bool InitializeWindowsUpdateMonitoring(TimeSpan sensorInterval, TimeSpan updateInterval, string specificPath = null);

        #endregion

        /// <summary>
        /// Creates the instance of <see cref="IInstantValueSensor{T}"/> where T is bool
        /// </summary>
        /// <param name="path">Sensor path to display in the tree</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <returns>A new instance of <see cref="IInstantValueSensor{T}"/> where T is bool</returns>
        IInstantValueSensor<bool> CreateBoolSensor(string path, string description = "");

        /// <summary>
        /// Creates the instance of <see cref="IInstantValueSensor{T}"/> where T is int
        /// </summary>
        /// <param name="path">Sensor path to display in the tree</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <returns>A new instance of <see cref="IInstantValueSensor{T}"/> where T is int</returns>
        IInstantValueSensor<int> CreateIntSensor(string path, string description = "");

        /// <summary>
        /// Creates the instance of <see cref="IInstantValueSensor{T}"/> where T is double
        /// </summary>
        /// <param name="path">Sensor path to display in the tree</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <returns>A new instance of <see cref="IInstantValueSensor{T}"/> where T is double</returns>
        IInstantValueSensor<double> CreateDoubleSensor(string path, string description = "");

        /// <summary>
        /// Creates the instance of <see cref="IInstantValueSensor{T}"/> where T is string
        /// </summary>
        /// <param name="path">Sensor path to display in the tree</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <returns>A new instance of <see cref="IInstantValueSensor{T}"/> where T is string</returns>
        IInstantValueSensor<string> CreateStringSensor(string path, string description = "");

        /// <summary>
        /// Creates the instance of <see cref="IInstantValueSensor{T}"/> where T is string
        /// </summary>
        /// <param name="path">Sensor path to display in the tree</param>
        /// <param name="fileName">Name of result file</param>
        /// <param name="extension">Extension of result file</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <returns>A new instance of <see cref="IInstantValueSensor{T}"/> where T is string</returns>
        IInstantValueSensor<string> CreateFileSensor(string path, string fileName, string extension = "txt", string description = "");

        Task SendFileAsync(string sensorPath, string filePath, SensorStatus status = SensorStatus.Ok, string comment = "");

        /// <summary>
        /// Creates the instance of <see cref="ILastValueSensor{T}"/> where T is bool
        /// </summary>
        /// <param name="path">Sensor path to display in the tree</param>
        /// <param name="defaultValue">The default value that is sent to a server if no other values passed</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <returns>A new instance of <see cref="ILastValueSensor{T}"/> where T is bool</returns>
        ILastValueSensor<bool> CreateLastValueBoolSensor(string path, bool defaultValue, string description = "");

        /// <summary>
        /// Creates the instance of <see cref="ILastValueSensor{T}"/> where T is int
        /// </summary>
        /// <param name="path">Sensor path to display in the tree</param>
        /// <param name="defaultValue">The default value that is sent to a server if no other values passed</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <returns>A new instance of <see cref="ILastValueSensor{T}"/> where T is int</returns>
        ILastValueSensor<int> CreateLastValueIntSensor(string path, int defaultValue, string description = "");

        /// <summary>
        /// Creates the instance of <see cref="ILastValueSensor{T}"/> where T is double
        /// </summary>
        /// <param name="path">Sensor path to display in the tree</param>
        /// <param name="defaultValue">The default value that is sent to a server if no other values passed</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <returns>A new instance of <see cref="ILastValueSensor{T}"/> where T is double</returns>
        ILastValueSensor<double> CreateLastValueDoubleSensor(string path, double defaultValue, string description = "");

        /// <summary>
        /// Creates the instance of <see cref="ILastValueSensor{T}"/> where T is string
        /// </summary>
        /// <param name="path">Sensor path to display in the tree</param>
        /// <param name="defaultValue">The default value that is sent to a server if no other values passed</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <returns>A new instance of <see cref="ILastValueSensor{T}"/> where T is string</returns>
        ILastValueSensor<string> CreateLastValueStringSensor(string path, string defaultValue, string description = "");

        #region Bar sensors

        /// <summary>
        /// Creates new BarSensor for collecting int values via specified parameters
        /// </summary>
        /// <param name="path">Sensor path in the tree</param>
        /// <param name="timeout">One bar contains the data for the specified period. Defaults to 5 minutes</param>
        /// <param name="smallPeriod">The frequency of sending bar updates to a server. Defaults to 15 sec</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <returns>A new instance of <see cref="IBarSensor{T}"/> where T is int, with specified parameters</returns>
        IBarSensor<int> CreateIntBarSensor(string path, int timeout = 300000, int smallPeriod = 15000, string description = "");

        /// <summary>
        /// Creates new BarSensor for collecting int values with timeout sent to 1 hour and smallPeriod
        /// set to 15 seconds (<see cref="CreateIntBarSensor"/>
        /// </summary>
        /// <param name="path">Sensor path in the tree</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <returns>A new instance of <see cref="IBarSensor{T}"/> where T is int, where timeout is 1 hr
        /// and smallPeriod is 15 sec</returns>
        IBarSensor<int> Create1HrIntBarSensor(string path, string description = "");

        /// <summary>
        /// Creates new BarSensor for collecting int values with timeout sent to 30 minutes and smallPeriod
        /// set to 15 seconds (<see cref="CreateIntBarSensor"/>
        /// </summary>
        /// <param name="path">Sensor path in the tree</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <returns>A new instance of <see cref="IBarSensor{T}"/> where T is int, where timeout is 30 min
        /// and smallPeriod is 15 sec</returns>
        IBarSensor<int> Create30MinIntBarSensor(string path, string description = "");

        /// <summary>
        /// Creates new BarSensor for collecting int values with timeout sent to 10 minutes and smallPeriod
        /// set to 15 seconds (<see cref="CreateIntBarSensor"/>
        /// </summary>
        /// <param name="path">Sensor path in the tree</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <returns>A new instance of <see cref="IBarSensor{T}"/> where T is int, where timeout is 10 min
        /// and smallPeriod is 15 sec</returns>
        IBarSensor<int> Create10MinIntBarSensor(string path, string description = "");

        /// <summary>
        /// Creates new BarSensor for collecting int values with timeout sent to 5 minutes and smallPeriod
        /// set to 15 seconds (<see cref="CreateIntBarSensor"/>
        /// </summary>
        /// <param name="path">Sensor path in the tree</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <returns>A new instance of <see cref="IBarSensor{T}"/> where T is int, where timeout is 5 min
        /// and smallPeriod is 15 sec</returns>
        IBarSensor<int> Create5MinIntBarSensor(string path, string description = "");

        /// <summary>
        /// Creates new BarSensor for collecting int values with timeout sent to 1 minute and smallPeriod
        /// set to 15 seconds (<see cref="CreateIntBarSensor"/>
        /// </summary>
        /// <param name="path">Sensor path in the tree</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <returns>A new instance of <see cref="IBarSensor{T}"/> where T is int, where timeout is 1 min
        /// and smallPeriod is 15 sec</returns>
        IBarSensor<int> Create1MinIntBarSensor(string path, string description = "");

        /// <summary>
        /// Creates new BarSensor for collecting double values via specified parameters
        /// </summary>
        /// <param name="path">Sensor path in the tree</param>
        /// <param name="timeout">One bar contains the data for the specified period. Defaults to 5 minutes</param>
        /// <param name="smallPeriod">The frequency of sending bar updates to a server. Defaults to 15 sec</param>
        /// <param name="precision">The precision applied to all characteristics calculations, defaults to 2</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <returns>A new instance of <see cref="IBarSensor{T}"/> where T is double, with specified parameters</returns>
        IBarSensor<double> CreateDoubleBarSensor(string path, int timeout = 300000, int smallPeriod = 15000, int precision = 2, string description = "");

        /// <summary>
        /// Creates new BarSensor for collecting int values with timeout sent to 1 hour and smallPeriod
        /// set to 15 seconds (<see cref="CreateDoubleBarSensor"/>
        /// </summary>
        /// <param name="path">Sensor path in the tree</param>
        /// <param name="precision">The precision applied to all characteristics calculations, defaults to 2</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <returns>A new instance of <see cref="IBarSensor{T}"/> where T is int, where timeout is 1 hr
        /// and smallPeriod is 15 sec</returns>
        IBarSensor<double> Create1HrDoubleBarSensor(string path, int precision = 2, string description = "");

        /// <summary>
        /// Creates new BarSensor for collecting int values with timeout sent to 30 minutes and smallPeriod
        /// set to 15 seconds (<see cref="CreateDoubleBarSensor"/>
        /// </summary>
        /// <param name="path">Sensor path in the tree</param>
        /// <param name="precision">The precision applied to all characteristics calculations, defaults to 2</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <returns>A new instance of <see cref="IBarSensor{T}"/> where T is int, where timeout is 30 min
        /// and smallPeriod is 15 sec</returns>
        IBarSensor<double> Create30MinDoubleBarSensor(string path, int precision = 2, string description = "");

        /// <summary>
        /// Creates new BarSensor for collecting int values with timeout sent to 10 minutes and smallPeriod
        /// set to 15 seconds (<see cref="CreateDoubleBarSensor"/>
        /// </summary>
        /// <param name="path">Sensor path in the tree</param>
        /// <param name="precision">The precision applied to all characteristics calculations, defaults to 2</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <returns>A new instance of <see cref="IBarSensor{T}"/> where T is int, where timeout is 10 min
        /// and smallPeriod is 15 sec</returns>
        IBarSensor<double> Create10MinDoubleBarSensor(string path, int precision = 2, string description = "");

        /// <summary>
        /// Creates new BarSensor for collecting int values with timeout sent to 5 minutes and smallPeriod
        /// set to 15 seconds (<see cref="CreateDoubleBarSensor"/>
        /// </summary>
        /// <param name="path">Sensor path in the tree</param>
        /// <param name="precision">The precision applied to all characteristics calculations, defaults to 2</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <returns>A new instance of <see cref="IBarSensor{T}"/> where T is int, where timeout is 5 min
        /// and smallPeriod is 15 sec</returns>
        IBarSensor<double> Create5MinDoubleBarSensor(string path, int precision = 2, string description = "");

        /// <summary>
        /// Creates new BarSensor for collecting int values with timeout sent to 1 minute and smallPeriod
        /// set to 15 seconds (<see cref="CreateDoubleBarSensor"/>
        /// </summary>
        /// <param name="path">Sensor path in the tree</param>
        /// <param name="precision">The precision applied to all characteristics calculations, defaults to 2</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <returns>A new instance of <see cref="IBarSensor{T}"/> where T is int, where timeout is 1 min
        /// and smallPeriod is 15 sec</returns>
        IBarSensor<double> Create1MinDoubleBarSensor(string path, int precision = 2, string description = "");

        #endregion

        #region Custom func sensors

        /// <summary>
        /// Create a new instance of <see cref="INoParamsFuncSensor{T}"/> with the specified parameters
        /// </summary>
        /// <typeparam name="T">The return type of the function <see cref="function"/></typeparam>
        /// <param name="path">Sensor path in the tree</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <param name="function">The function that is invoked</param>
        /// <param name="interval">The <see cref="function"/> is invoked every time the interval passes</param>
        /// <returns>A new instance of <see cref="INoParamsFuncSensor{T}"/> with interval set via TimeSpan</returns>
        INoParamsFuncSensor<T> CreateNoParamsFuncSensor<T>(string path, string description, Func<T> function, TimeSpan interval);

        /// <summary>
        /// Create a new instance of <see cref="INoParamsFuncSensor{T}"/> with the specified parameters
        /// </summary>
        /// <typeparam name="T">The return type of the function <see cref="function"/></typeparam>
        /// <param name="path">Sensor path in the tree</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <param name="function">The function that is invoked</param>
        /// <param name="millisecondsInterval">The interval set in milliseconds, defaults to 15 sec</param>
        /// <returns>A new instance of <see cref="INoParamsFuncSensor{T}"/> with interval set via milliseconds</returns>
        INoParamsFuncSensor<T> CreateNoParamsFuncSensor<T>(string path, string description, Func<T> function, int millisecondsInterval = 15000);

        /// <summary>
        /// Create a new instance of <see cref="INoParamsFuncSensor{T}"/> with 1 minute interval 
        /// </summary>
        /// <typeparam name="T">The return type of the function <see cref="function"/></typeparam>
        /// <param name="path">Sensor path in the tree</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <param name="function">The function that is invoked</param>
        /// <returns>A new instance of <see cref="INoParamsFuncSensor{T}"/> with interval set to 1 min</returns>
        INoParamsFuncSensor<T> Create1MinNoParamsFuncSensor<T>(string path, string description, Func<T> function);

        /// <summary>
        /// Create a new instance of <see cref="INoParamsFuncSensor{T}"/> with 5 minutes interval 
        /// </summary>
        /// <typeparam name="T">The return type of the function <see cref="function"/></typeparam>
        /// <param name="path">Sensor path in the tree</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <param name="function">The function that is invoked</param>
        /// <returns>A new instance of <see cref="INoParamsFuncSensor{T}"/> with interval set to 5 min</returns>
        INoParamsFuncSensor<T> Create5MinNoParamsFuncSensor<T>(string path, string description, Func<T> function);

        /// <summary>
        /// Create a new instance of <see cref="IParamsFuncSensor{T, U}"/> with the specified parameters
        /// </summary>
        /// <typeparam name="T">The return type of the function <see cref="function"/></typeparam>
        /// <typeparam name="U">The <see cref="function"/> must accept <see cref="List{U}"/> as an input parameter</typeparam>
        /// <param name="path">Sensor path in the tree</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <param name="function">The function that is invoked</param>
        /// <param name="interval">The <see cref="function"/> is invoked every time the interval passes</param>
        /// <returns>A new instance of <see cref="IParamsFuncSensor{T, U}"/> with interval set via TimeSpan</returns>
        IParamsFuncSensor<T, U> CreateParamsFuncSensor<T, U>(string path, string description, Func<List<U>, T> function, TimeSpan interval);

        /// <summary>
        /// Create a new instance of <see cref="IParamsFuncSensor{T, U}"/> with the specified parameters
        /// </summary>
        /// <typeparam name="T">The return type of the function <see cref="function"/></typeparam>
        /// <typeparam name="U">The <see cref="function"/> must accept <see cref="List{U}"/> as an input parameter</typeparam>
        /// <param name="path">Sensor path in the tree</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <param name="function">The function that is invoked</param>
        /// <param name="millisecondsInterval">The interval set in milliseconds, defaults to 15 sec</param>
        /// <returns>A new instance of <see cref="IParamsFuncSensor{T, U}"/> with interval set via milliseconds</returns>
        IParamsFuncSensor<T, U> CreateParamsFuncSensor<T, U>(string path, string description, Func<List<U>, T> function, int millisecondsInterval = 15000);

        /// <summary>
        /// Create a new instance of <see cref="IParamsFuncSensor{T, U}"/> with 1 minute interval 
        /// </summary>
        /// <typeparam name="T">The return type of the function <see cref="function"/></typeparam>
        /// <typeparam name="U">The <see cref="function"/> must accept <see cref="List{U}"/> as an input parameter</typeparam>
        /// <param name="path">Sensor path in the tree</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <param name="function">The function that is invoked</param>
        /// <returns>A new instance of <see cref="IParamsFuncSensor{T, U}"/> with interval set to 1 min</returns>
        IParamsFuncSensor<T, U> Create1MinParamsFuncSensor<T, U>(string path, string description, Func<List<U>, T> function);

        /// <summary>
        /// Create a new instance of <see cref="IParamsFuncSensor{T, U}"/> with 5 minutes interval 
        /// </summary>
        /// <typeparam name="T">The return type of the function <see cref="function"/></typeparam>
        /// <typeparam name="U">The <see cref="function"/> must accept <see cref="List{U}"/> as an input parameter</typeparam>
        /// <param name="path">Sensor path in the tree</param>
        /// <param name="description">Possible sensor description, empty by default</param>
        /// <param name="function">The function that is invoked</param>
        /// <returns>A new instance of <see cref="IParamsFuncSensor{T, U}"/> with interval set to 5 min</returns>
        IParamsFuncSensor<T, U> Create5MinParamsFuncSensor<T, U>(string path, string description, Func<List<U>, T> function);

        #endregion
        
        /// <summary>
        /// The event is fired after the values queue (current capacity is 100000 items) overflows
        /// </summary>
        [Obsolete("Will never be called")]
        event EventHandler ValuesQueueOverflow;
    }
}