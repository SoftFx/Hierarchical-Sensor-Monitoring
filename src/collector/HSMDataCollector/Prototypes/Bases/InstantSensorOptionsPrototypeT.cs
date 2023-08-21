namespace HSMDataCollector.Prototypes
{
    internal abstract class InstantSensorOptionsPrototype<T> : Options.InstantSensorOptions
        where T : Options.InstantSensorOptions, new()
    {
        protected abstract string SensorName { get; }

        protected abstract string Category { get; }


        protected InstantSensorOptionsPrototype()
        {
            Path = DefaultPrototype.BuildDefaultPath(Category, SensorName);
            EnableForGrafana = true;
        }


        public virtual T Get(T customOptions)
        {
            var options = DefaultPrototype.Merge(this, customOptions);

            options.Alerts = customOptions?.Alerts ?? Alerts;

            return (T)options;
        }
    }
}