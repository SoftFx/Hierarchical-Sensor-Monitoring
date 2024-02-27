using HSMDataCollector.Options;
using System;

namespace HSMDataCollector.Prototypes
{
    internal abstract class MonitoringInstantSensorOptionsPrototype<T> : InstantSensorOptionsPrototype<T>
        where T : MonitoringInstantSensorOptions, new()
    {
        protected abstract TimeSpan DefaultPostDataPeriod { get; }


        public override T Get(T customOptions)
        {
            var options = base.Get(customOptions);

            options.PostDataPeriod = customOptions?.PostDataPeriod ?? DefaultPostDataPeriod;

            return options;
        }
    }
}