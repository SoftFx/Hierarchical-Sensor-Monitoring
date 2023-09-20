using HSMDataCollector.Options;
using HSMSensorDataObjects;

namespace HSMDataCollector.Prototypes.Collections
{
    internal class WindowsLogPrototype : InstantSensorOptionsPrototype<WindowsLogsOptions>
    {
        private string _sensorName;
        
        protected override string SensorName => _sensorName;

        protected override string Category => "Windows Logs";

        public override WindowsLogsOptions Get(WindowsLogsOptions customOptions)
        {
            _sensorName = customOptions.IsError ? "Windows Error Logs" : customOptions.IsWarning ? "Windows Warning Logs" : string.Empty;

            var options = base.Get(customOptions);

            options.Type = SensorType.StringSensor;
            
            return options;
        }


        public WindowsLogPrototype() : base()
        {
            Description = "some desc";
            IsSingletonSensor = true;
        }
    }
}