using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors.Other;
using HSMDataCollector.Options;
using System;

namespace HSMDataCollector.DefaultSensors
{
    internal abstract class DefaultSensorsCollection
    {
        private const string NotSupportedSensor = "Sensor is not supported for current OS";

        private static readonly NotSupportedException _notSupportedException = new NotSupportedException(NotSupportedSensor);

        private readonly SensorsStorage _storage;
        protected readonly PrototypesCollection _prototype;


        internal CollectorStatusSensor StatusSensor { get; private set; }

        internal ProductVersionSensor ProductVersion { get; private set; }

        internal ProductVersionSensor CollectorVersion { get; private set; }


        protected abstract bool IsCorrectOs { get; }


        protected DefaultSensorsCollection(SensorsStorage storage, PrototypesCollection prototype)
        {
            _storage = storage;
            _prototype = prototype;
        }


        protected DefaultSensorsCollection AddCollectorAliveCommon(CollectorMonitoringInfoOptions options)
        {
            return Register(new CollectorAlive(options));
            //return Register(new CollectorAlive(_prototype.CollectorAlive.Get(options)));
        }

        protected DefaultSensorsCollection AddCollectorVersionCommon(CollectorInfoOptions options = null)
        {
            if (CollectorVersion != null)
                return this;

            CollectorVersion = new ProductVersionSensor(_prototype.CollectorVersion.Get(null));

            return Register(CollectorVersion);
        }

        protected DefaultSensorsCollection AddCollectorStatusCommon(CollectorInfoOptions options)
        {
            if (StatusSensor != null)
                return this;

            StatusSensor = new CollectorStatusSensor(options);
            //StatusSensor = new CollectorStatusSensor(_prototype.CollectorStatus.GetAndFill(options));

            return Register(StatusSensor);
        }

        protected DefaultSensorsCollection AddFullCollectorMonitoringCommon(CollectorMonitoringInfoOptions monitoringOptions)
        {
            //monitoringOptions = _prototype.CollectorAlive.GetAndFill(monitoringOptions);

            var options = new CollectorInfoOptions() { Path = monitoringOptions.Path };
            //var options = _prototype.CollectorStatus.GetAndFill(new CollectorInfoOptions() { NodePath = monitoringOptions.NodePath });

            return AddCollectorAliveCommon(monitoringOptions).AddCollectorVersionCommon(options).AddCollectorStatusCommon(options);
        }

        protected DefaultSensorsCollection AddProductVersionCommon(VersionSensorOptions options)
        {
            if (ProductVersion != null)
                return this;

            ProductVersion = new ProductVersionSensor(options);
            //ProductVersion = new ProductVersionSensor(_prototype.ProductVersion.GetAndFill(options));

            return Register(ProductVersion);
        }

        protected DefaultSensorsCollection Register(SensorBase sensor)
        {
            if (!IsCorrectOs)
                throw _notSupportedException;

            _storage.Register(sensor);

            return this;
        }
    }
}
