using System;
using System.Collections.Generic;
using HSMDataCollector.Alerts;
using HSMDataCollector.Options;
using HSMSensorDataObjects;


namespace HSMDataCollector.Prototypes
{
    internal abstract class EnumSensorOptionsPrototype<T> : EnumSensorOptions where T : EnumSensorOptions, new()
    {
        protected abstract string SensorName { get; }

        protected virtual string Category { get; }


        protected EnumSensorOptionsPrototype()
        {
            Alerts = new List<InstantAlertTemplate>();

            TTL = TimeSpan.MaxValue; //Never
            EnableForGrafana = true;
            Type = SensorType.EnumSensor;
        }


        public virtual T Get(T customOptions)
        {
            var options = DefaultPrototype.Merge(this, customOptions);

            options.Path = RebuildPath();
            options.Alerts = customOptions?.Alerts ?? Alerts;

            return options;
        }


        protected string RebuildPath() => DefaultPrototype.RevealDefaultPath(this, Category, SensorName);


    }
}