using HSMDataCollector.Options;
using System;

namespace HSMDataCollector.Prototypes
{
    internal abstract class BarSensorOptionsPrototype<T> : BarSensorOptions
        where T : BarSensorOptions, new()
    {
        protected abstract string SensorName { get; }

        protected virtual string Category { get; }


        protected BarSensorOptionsPrototype()
        {
            EnableForGrafana = true;

            TTL = TimeSpan.MaxValue; //Never
        }


        public virtual T Get(T customOptions)
        {
            var options = DefaultPrototype.Merge(this, customOptions);

            options.Path = RebuildPath();
            options.Alerts = customOptions?.Alerts ?? Alerts;

            options.PostDataPeriod = customOptions?.PostDataPeriod ?? PostDataPeriod;
            options.BarTickPeriod = customOptions?.BarTickPeriod ?? BarTickPeriod;
            options.BarPeriod = customOptions?.BarPeriod ?? BarPeriod;

            options.Precision = customOptions?.Precision ?? Precision;

            return options;
        }


        protected string RebuildPath() => DefaultPrototype.RevealDefaultPath(this, Category, SensorName);
    }
}