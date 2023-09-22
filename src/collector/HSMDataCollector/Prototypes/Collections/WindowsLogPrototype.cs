using HSMDataCollector.Options;
using HSMSensorDataObjects;

namespace HSMDataCollector.Prototypes.Collections
{
    internal abstract class WindowsLogPrototype : InstantSensorOptionsPrototype<InstantSensorOptions>
    {
        protected override string Category => "Windows OS info/Windows Logs";


        protected WindowsLogPrototype() : base()
        {
            Description = "some desc";
            IsSingletonSensor = true;
            IsComputerSensor = true;
            
            Type = SensorType.StringSensor;
        }
    }

    internal class WindowsErrorLogsPrototype : WindowsLogPrototype
    {
        protected override string SensorName => "Windows Error Logs";
    }
    
    internal class WindowsWarningLogsPrototype : WindowsLogPrototype
    {
        protected override string SensorName => "Windows Warning Logs";
    }
}