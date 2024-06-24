﻿using System;
using System.Collections.Generic;
using HSMDataCollector.Alerts;
using HSMDataCollector.Options;


namespace HSMDataCollector.Prototypes
{
    internal abstract class InstantSensorOptionsPrototype<T> : InstantSensorOptions
        where T : InstantSensorOptions, new()
    {
        protected const string WindowsOsInfo = "Windows OS info";


        protected abstract string SensorName { get; }

        protected virtual string Category { get; }


        protected InstantSensorOptionsPrototype()
        {
            Alerts = new List<InstantAlertTemplate>();

            TTL = TimeSpan.MaxValue; //Never
            EnableForGrafana = true;
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