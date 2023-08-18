using HSMDataCollector.Options;
using System;
using System.Linq;

namespace HSMDataCollector.Prototypes
{
    interface IOptionsPrototype : IDefaultOptions
    {

    }

    interface IDefaultOptions
    {
        //string Category { get; }
    }


    internal abstract class BarSensorOptionsPrototype<T> : BarSensorOptions, IOptionsPrototype
        where T : BarSensorOptions, new()
    {
        protected abstract string SensorName { get; }

        protected abstract string Category { get; }


        protected BarSensorOptionsPrototype()
        {
            Path = DefaultSensorPrototype.BuildPath(Category, Path);
            EnableForGrafana = true;
        }


        public virtual T Get(T customOptions)
        {
            var options = DefaultSensorPrototype.Merge<BarSensorOptions>(this, customOptions);

            options.Alerts = customOptions.Alerts ?? Alerts;

            options.PostDataPeriod = customOptions?.PostDataPeriod ?? PostDataPeriod;
            options.BarTickPeriod = customOptions?.BarTickPeriod ?? BarTickPeriod;
            options.BarPeriod = customOptions?.BarPeriod ?? BarPeriod;

            options.Precision = customOptions?.Precision ?? Precision;

            return (T)options;
        }
    }

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


    internal abstract class InstantSensorOptionsPrototype<T> : InstantSensorOptions, IOptionsPrototype
        where T : InstantSensorOptions, new()
    {
        protected abstract string SensorName { get; }

        protected abstract string Category { get; }


        protected InstantSensorOptionsPrototype()
        {
            Path = DefaultSensorPrototype.BuildPath(Category, Path);
            EnableForGrafana = true;
        }


        public virtual T Get(T customOptions)
        {
            var options = DefaultSensorPrototype.Merge<InstantSensorOptions>(this, customOptions);

            options.Alerts = customOptions.Alerts ?? Alerts;

            return (T)options;
        }
    }


    internal static class DefaultSensorPrototype
    {
        private const string PathSeparator = "/";
        private const string SystemPath = ".Default";


        internal static T Merge<T>(T defaultOptions, T customOptions) where T : SensorOptions, new() =>
            new T()
            {
                Module = defaultOptions.Module,
                Path = defaultOptions.Path,
                Type = defaultOptions.Type,

                TtlAlert = customOptions?.TtlAlert ?? defaultOptions.TtlAlert,

                Description = customOptions?.Description ?? defaultOptions.Description,
                SensorUnit = customOptions?.SensorUnit ?? defaultOptions.SensorUnit,

                KeepHistory = customOptions?.KeepHistory ?? defaultOptions.KeepHistory,
                SelfDestroy = customOptions?.SelfDestroy ?? defaultOptions.SelfDestroy,
                TTL = customOptions?.TTL ?? defaultOptions.TTL,

                EnableForGrafana = customOptions?.EnableForGrafana ?? defaultOptions.EnableForGrafana,
                AggregateData = customOptions?.AggregateData ?? defaultOptions.AggregateData,
            };


        internal static string BuildPath(params string[] parts) => string.Join(PathSeparator, SystemPath, parts.Select(u => !string.IsNullOrEmpty(u)));
    }
}