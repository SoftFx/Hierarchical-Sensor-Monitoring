using HSMDataCollector.Options;
using System;

namespace HSMDataCollector.Prototypes
{
    internal abstract class InstantSensorOptionsPrototype<T> : InstantSensorOptions
        where T : InstantSensorOptions, new()
    {
        protected abstract string SensorName { get; }

        protected virtual string Category { get; }


        protected InstantSensorOptionsPrototype()
        {
            EnableForGrafana = true;

            TTL = TimeSpan.MaxValue; //Never
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