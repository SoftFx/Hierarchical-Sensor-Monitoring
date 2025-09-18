using System;
using System.Collections.Generic;
using HSMDataCollector.Alerts;
using HSMDataCollector.Options;
using HSMSensorDataObjects.SensorRequests;


namespace HSMDataCollector.Prototypes
{
    internal abstract class BarSensorOptionsPrototype<T> : BarSensorOptions
        where T : BarSensorOptions, new()
    {
        protected const string BaseDescription = "The sensor sends information about {0} with a period of {1} and aggregated into bars of {2}. The information is read using " +
                                                 "[**Performance counter**](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.performancecounter?view=netframework-4.7.2) by path *{3}*";


        protected abstract string SensorName { get; }

        protected virtual string Category { get; }


        protected BarSensorOptionsPrototype()
        {
            Alerts = new List<BarAlertTemplate>();

            TTL = TimeSpan.MaxValue; //Never
            EnableForGrafana = true;
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