using HSMDataCollector.Options;

namespace HSMDataCollector.Prototypes
{
    internal abstract class InstantSensorOptionsPrototype<T> : InstantSensorOptions
        where T : InstantSensorOptions, new()
    {
        protected abstract string SensorName { get; }

        protected abstract string Category { get; }


        protected InstantSensorOptionsPrototype()
        {
            Path = DefaultPrototype.BuildDefaultPath(Category, Path);
            EnableForGrafana = true;
        }


        public virtual T Get(T customOptions)
        {
            var options = DefaultPrototype.Merge<InstantSensorOptions>(this, customOptions);

            options.Alerts = customOptions.Alerts ?? Alerts;

            return (T)options;
        }
    }
}