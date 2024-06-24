using System.Collections.Generic;
using System.Diagnostics;
using HSMDataCollector.Alerts;
using HSMDataCollector.DefaultSensors.Windows;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;


namespace HSMDataCollector.Prototypes
{
    internal abstract class ProcessBasePrototype : BarSensorOptionsPrototype<BarSensorOptions>
    {
        protected readonly string _processName;


        protected override string Category { get; }


        protected ProcessBasePrototype() : base()
        {
            _processName = Process.GetCurrentProcess().ProcessName;

            Category = $"Process {_processName}";
            Statistics = StatisticsOptions.EMA;
            Type = SensorType.DoubleBarSensor;
        }
    }


    internal sealed class ProcessCpuPrototype : ProcessBasePrototype
    {
        protected override string SensorName => "Process CPU";


        public ProcessCpuPrototype() : base()
        {
            Description = $"This sensor sends information about **{_processName}** process CPUs with a period of {PostDataPeriod.ToReadableView()} and aggregated into bars of {BarPeriod.ToReadableView()}.  \n" +
            "CPU usage indicates the total percentage of processing power" +
            " exhausted to process data and run various programs on a network device, " +
            "server, or computer at any given point. More info can be found [**here**](https://en.wikipedia.org/wiki/Central_processing_unit).";

            Statistics = StatisticsOptions.None;
            SensorUnit = Unit.Percents;
        }
    }


    internal sealed class ProcessMemoryPrototype : ProcessBasePrototype
    {
        protected override string SensorName => "Process memory";


        public ProcessMemoryPrototype() : base()
        {
            Description = $"This sensor sends information about **{_processName}** process RAM. {GetBarOptionsInfo()}  \n" +
            "Free memory, which is memory available to the operating system," +
            " is defined as free and cache pages. The remainder is active memory, which is memory " +
            "currently in use by the operating system. More info can be found [**here**](https://en.wikipedia.org/wiki/Random-access_memory).";

            SensorUnit = Unit.MB;

            Alerts = new List<BarAlertTemplate>()
            {
                AlertsFactory.IfEmaMean(AlertOperation.GreaterThan, 30.GigobytesToMegabytes())
                             .ThenSendInstantHourlyScheduledNotification($"[$product]$path $property $operation $target $unit")
                             .AndSetIcon(AlertIcon.Warning).Build(),
            };
        }
    }


    internal sealed class ProcessThreadCountPrototype : ProcessBasePrototype
    {
        protected override string SensorName => "Process thread count";


        public ProcessThreadCountPrototype() : base()
        {
            Description = $"This sensor sends information about **{_processName}** process threads count. {GetBarOptionsInfo()} \n" +
            "A thread is the basic unit to which the operating system allocates processor time. A thread can execute any part of the process code, " +
            "including parts currently being executed by another thread.  \n" +
            "More information about processes and threads you can find [**here**](https://learn.microsoft.com/en-us/windows/win32/procthread/processes-and-threads).";

            Alerts = new List<BarAlertTemplate>()
            {
                AlertsFactory.IfEmaMean(AlertOperation.GreaterThan, 2000)
                             .ThenSendInstantHourlyScheduledNotification("[$product]$path $property $operation $target")
                             .AndSetIcon(AlertIcon.Warning).Build(),
            };
        }
    }


    internal sealed class ProcessTimeInGCPrototype : ProcessBasePrototype
    {
        protected override string SensorName => "Process time in GC";


        public ProcessTimeInGCPrototype() : base()
        {

#if NET6_0_OR_GREATER

            Description = $"The sensor sends information about {SensorName}. {GetBarOptionsInfo()} The information is read using " +
                "[**ETW events**](https://learn.microsoft.com/en-us/dotnet/framework/performance/garbage-collection-etw-events).";
#endif

            SensorUnit = Unit.Percents;

            Alerts = new List<BarAlertTemplate>()
            {
                AlertsFactory.IfEmaMean(AlertOperation.GreaterThan, 50)
                             .ThenSendInstantHourlyScheduledNotification("[$product]$path $property $operation $target")
                             .AndSetIcon(AlertIcon.Warning).Build(),
            };
        }


        public override BarSensorOptions Get(BarSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            if (options.Description == null)
                options.Description = string.Format(BaseDescription, SensorName, options.PostDataPeriod.ToReadableView(), options.BarPeriod.ToReadableView(), $"{WindowsTimeInGCBase.Category}/{WindowsTimeInGCBase.Counter}");

            return options;
        }
    }
}
