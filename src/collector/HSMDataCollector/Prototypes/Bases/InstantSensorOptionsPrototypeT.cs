using HSMDataCollector.Options;
using System;

namespace HSMDataCollector.Prototypes
{
    internal abstract class InstantSensorOptionsPrototype<T> : InstantSensorOptions
        where T : InstantSensorOptions, new()
    {
        protected abstract string SensorName { get; }

        protected abstract string Category { get; }


        protected InstantSensorOptionsPrototype()
        {
            Path = DefaultPrototype.BuildDefaultPath(Category, SensorName);
            EnableForGrafana = true;

            TTL = TimeSpan.MaxValue; //Never
        }


        public virtual T Get(T customOptions)
        {
            var options = DefaultPrototype.Merge(this, customOptions);

            options.Alerts = customOptions?.Alerts ?? Alerts;

            return options;
        }
    }
}