using System;
using HSMDataCollector.Options;
using HSMSensorDataObjects;


namespace HSMDataCollector.Prototypes.Collections
{
    internal class WindowsApplicationErrorLogsPrototype : WindowsLogPrototype
    {
        protected override string EventViewerCategory => "Application";

        protected override string Status => "Error";
    }


    internal class WindowsSystemErrorLogsPrototype : WindowsLogPrototype
    {
        protected override string EventViewerCategory => "System";

        protected override string Status => "Error";
    }


    internal class WindowsApplicationWarningLogsPrototype : WindowsLogPrototype
    {
        protected override string EventViewerCategory => "Application";

        protected override string Status => "Warning";
    }


    internal class WindowsSystemWarningLogsPrototype : WindowsLogPrototype
    {
        protected override string EventViewerCategory => "System";

        protected override string Status => "Warning";
    }


    internal abstract class WindowsLogPrototype : InstantSensorOptionsPrototype<InstantSensorOptions>
    {
        internal const string BaseDescription = "The sensor reads Windows Logs and sends all logs with **{0}** status from **{1}** category. The information is read using " +
                                                "[**Event log**](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.eventlog?view=dotnet-plat-ext-7.0)";

        protected abstract string EventViewerCategory { get; }

        protected abstract string Status { get; }


        protected override string SensorName => $"Windows {Status} Logs ({EventViewerCategory})";

        protected override string Category => WindowsOsInfo;


        protected WindowsLogPrototype() : base()
        {
            Description = string.Format(BaseDescription, Status, EventViewerCategory);
            IsComputerSensor = true;

            Type = SensorType.StringSensor;
            AggregateData = true;

            TTL = TimeSpan.MaxValue;
        }
    }
}