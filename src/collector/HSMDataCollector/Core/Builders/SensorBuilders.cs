using System;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;


namespace HSMDataCollector.Core
{
    /// <summary>
    /// Fluent entry points for creating sensors. These are a thin, portable facade over the existing
    /// options-based <c>IDataCollector.CreateXxx(path, options)</c> methods — the verbose per-type
    /// overloads remain available and unchanged. The builder collects an options object and dispatches
    /// to the correct factory method at <c>Build()</c>, so the same mental model
    /// (path → value type → kind → options) maps 1:1 onto a non-.NET port.
    ///
    /// <code>
    /// var cpu  = collector.InstantSensor&lt;double&gt;("cpu").Description("CPU %").Build();
    /// var rps  = collector.RateSensor("requests").PostPeriod(TimeSpan.FromMinutes(1)).Build();
    /// var lat  = collector.BarSensor&lt;double&gt;("latency").BarPeriod(TimeSpan.FromMinutes(5)).Precision(3).Build();
    /// </code>
    /// </summary>
    public static class SensorBuilderExtensions
    {
        /// <summary>Begins building an instant value sensor of type <typeparamref name="T"/>.</summary>
        public static InstantSensorBuilder<T> InstantSensor<T>(this IDataCollector collector, string path)
            => new InstantSensorBuilder<T>(collector, path);

        /// <summary>Begins building a bar (aggregated) sensor of type <typeparamref name="T"/> (int or double).</summary>
        public static BarSensorBuilder<T> BarSensor<T>(this IDataCollector collector, string path)
            where T : struct
            => new BarSensorBuilder<T>(collector, path);

        /// <summary>Begins building a rate sensor (values-per-second over a window).</summary>
        public static RateSensorBuilder RateSensor(this IDataCollector collector, string path)
            => new RateSensorBuilder(collector, path);
    }


    /// <summary>
    /// Fluent builder for an instant value sensor. Supported <typeparamref name="T"/>: bool, int, double,
    /// string, <see cref="Version"/>, <see cref="TimeSpan"/>.
    /// </summary>
    public sealed class InstantSensorBuilder<T>
    {
        private readonly IDataCollector _collector;
        private readonly string _path;
        private readonly InstantSensorOptions _options = new InstantSensorOptions();

        internal InstantSensorBuilder(IDataCollector collector, string path)
        {
            _collector = collector ?? throw new ArgumentNullException(nameof(collector));
            _path = path;
        }

        public InstantSensorBuilder<T> Description(string description)
        {
            _options.Description = description;
            return this;
        }

        public InstantSensorBuilder<T> Ttl(TimeSpan ttl)
        {
            _options.TTL = ttl;
            return this;
        }

        public InstantSensorBuilder<T> KeepHistory(TimeSpan keepHistory)
        {
            _options.KeepHistory = keepHistory;
            return this;
        }

        public InstantSensorBuilder<T> Priority(bool isPriority = true)
        {
            _options.IsPrioritySensor = isPriority;
            return this;
        }

        /// <summary>Escape hatch for any option not exposed as a fluent setter.</summary>
        public InstantSensorBuilder<T> Configure(Action<InstantSensorOptions> configure)
        {
            configure?.Invoke(_options);
            return this;
        }

        public IInstantValueSensor<T> Build()
        {
            var type = typeof(T);

            if (type == typeof(bool))
                return Cast(_collector.CreateBoolSensor(_path, _options));
            if (type == typeof(int))
                return Cast(_collector.CreateIntSensor(_path, _options));
            if (type == typeof(double))
                return Cast(_collector.CreateDoubleSensor(_path, _options));
            if (type == typeof(string))
                return Cast(_collector.CreateStringSensor(_path, _options));
            if (type == typeof(Version))
                return Cast(_collector.CreateVersionSensor(_path, _options));
            if (type == typeof(TimeSpan))
                return Cast(_collector.CreateTimeSensor(_path, _options));

            throw new NotSupportedException(
                $"Instant sensor of type '{type.Name}' is not supported. Supported types: bool, int, double, string, Version, TimeSpan.");
        }

        // T is verified to match the created sensor's type before this is called, so the object bridge is safe.
        private static IInstantValueSensor<T> Cast(object sensor) => (IInstantValueSensor<T>)sensor;
    }


    /// <summary>
    /// Fluent builder for a bar (aggregated) sensor. Supported <typeparamref name="T"/>: int, double.
    /// </summary>
    public sealed class BarSensorBuilder<T> where T : struct
    {
        private readonly IDataCollector _collector;
        private readonly string _path;
        private readonly BarSensorOptions _options = new BarSensorOptions();

        internal BarSensorBuilder(IDataCollector collector, string path)
        {
            _collector = collector ?? throw new ArgumentNullException(nameof(collector));
            _path = path;
        }

        /// <summary>The time span one bar aggregates (default 5 minutes).</summary>
        public BarSensorBuilder<T> BarPeriod(TimeSpan barPeriod)
        {
            _options.BarPeriod = barPeriod;
            return this;
        }

        /// <summary>How often partial bar updates are sent to the server (default 15 seconds).</summary>
        public BarSensorBuilder<T> PostPeriod(TimeSpan postPeriod)
        {
            _options.PostDataPeriod = postPeriod;
            return this;
        }

        /// <summary>How often a value is sampled into the current bar (default 5 seconds).</summary>
        public BarSensorBuilder<T> TickPeriod(TimeSpan tickPeriod)
        {
            _options.BarTickPeriod = tickPeriod;
            return this;
        }

        /// <summary>Rounding precision for computed bar characteristics (default 2).</summary>
        public BarSensorBuilder<T> Precision(int precision)
        {
            _options.Precision = precision;
            return this;
        }

        public BarSensorBuilder<T> Description(string description)
        {
            _options.Description = description;
            return this;
        }

        /// <summary>Escape hatch for any option not exposed as a fluent setter.</summary>
        public BarSensorBuilder<T> Configure(Action<BarSensorOptions> configure)
        {
            configure?.Invoke(_options);
            return this;
        }

        public IBarSensor<T> Build()
        {
            var type = typeof(T);

            if (type == typeof(int))
                return (IBarSensor<T>)_collector.CreateIntBarSensor(_path, _options);
            if (type == typeof(double))
                return (IBarSensor<T>)_collector.CreateDoubleBarSensor(_path, _options);

            throw new NotSupportedException(
                $"Bar sensor of type '{type.Name}' is not supported. Supported types: int, double.");
        }
    }


    /// <summary>
    /// Fluent builder for a rate sensor (reports the rate of supplied values per second over a window).
    /// </summary>
    public sealed class RateSensorBuilder
    {
        private readonly IDataCollector _collector;
        private readonly string _path;
        private readonly RateSensorOptions _options = new RateSensorOptions();

        internal RateSensorBuilder(IDataCollector collector, string path)
        {
            _collector = collector ?? throw new ArgumentNullException(nameof(collector));
            _path = path;
        }

        /// <summary>The window over which the rate is computed and sent (default 1 minute).</summary>
        public RateSensorBuilder PostPeriod(TimeSpan postPeriod)
        {
            _options.PostDataPeriod = postPeriod;
            return this;
        }

        public RateSensorBuilder Description(string description)
        {
            _options.Description = description;
            return this;
        }

        /// <summary>Escape hatch for any option not exposed as a fluent setter.</summary>
        public RateSensorBuilder Configure(Action<RateSensorOptions> configure)
        {
            configure?.Invoke(_options);
            return this;
        }

        public IMonitoringRateSensor Build() => _collector.CreateRateSensor(_path, _options);
    }
}
