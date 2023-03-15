using System;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Other
{
    internal sealed class ProductVersion : MonitoringSensorBase<string>
    {
        private readonly string _version;
        
        
        protected override string SensorName => "Product Version";

        public ProductVersion(VersionSensorOptions options) : base(options)
        {
            Console.WriteLine(options.NodePath + '/' + SensorName);
            _version = options.Version;
        }
        protected override string GetValue() => _version;
    }
}