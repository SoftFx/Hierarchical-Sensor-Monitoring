using HSMDataCollector.PublicInterface;


namespace HSMDataCollector.Core
{
    /// <summary>
    /// Change a created sensor's description (#1482) through the handle a <c>Create...Sensor</c> call
    /// returned. Each overload delegates to <see cref="IDescribableSensor.SetDescription(string)"/> when the
    /// sensor has that capability (every collector-created sensor does) and returns <c>false</c> for a
    /// <c>null</c> handle or an external implementation without it. See
    /// <see cref="IDescribableSensor.SetDescription(string)"/> for when the new text reaches the server.
    /// Extension methods rather than new interface members, so external implementations of the
    /// sensor interfaces keep compiling; in <c>HSMDataCollector.Core</c> next to <see cref="DataCollector"/>
    /// so a caller that creates the collector already has them in scope.
    /// </summary>
    public static class SensorDescriptionExtensions
    {
        /// <summary>Replaces the description of an instant, last-value, rate or file sensor and re-registers it.</summary>
        public static bool SetDescription<T>(this IInstantValueSensor<T> sensor, string description) =>
            SetDescriptionCore(sensor, description);

        /// <summary>Replaces the description of a bar sensor and re-registers it.</summary>
        public static bool SetDescription<T>(this IBarSensor<T> sensor, string description) where T : struct =>
            SetDescriptionCore(sensor, description);

        /// <summary>Replaces the description of the service-commands sensor and re-registers it.</summary>
        public static bool SetDescription(this IServiceCommandsSensor sensor, string description) =>
            SetDescriptionCore(sensor, description);

        /// <summary>Replaces the description of a function sensor and re-registers it.</summary>
        public static bool SetDescription(this IBaseFuncSensor sensor, string description) =>
            SetDescriptionCore(sensor, description);

        private static bool SetDescriptionCore(object sensor, string description) =>
            sensor is IDescribableSensor describable && describable.SetDescription(description);
    }
}
