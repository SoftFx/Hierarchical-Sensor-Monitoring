using System;


namespace HSMDataCollector.Core
{
    public static class CollectorLifecycleExtensions
    {
        /// <summary>
        /// Registers a lifecycle listener when the collector exposes the optional observer
        /// capability, without adding a required member to <see cref="IDataCollector"/>.
        /// </summary>
        public static IDataCollector AddLifecycleListener(this IDataCollector collector, ILifecycleListener listener)
        {
            if (collector == null)
                throw new ArgumentNullException(nameof(collector));

            if (collector is ILifecycleObservableCollector observable)
                return observable.AddLifecycleListener(listener);

            return collector;
        }
    }
}
