using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors.Other;
using HSMDataCollector.Options;
using System;

namespace HSMDataCollector.DefaultSensors
{
    internal abstract class DefaultSensorsCollection : IDisposable
    {
        private const string NotSupportedSensor = "Sensor is not supported for current OS";

        private static readonly NotSupportedException _notSupportedException = new NotSupportedException(NotSupportedSensor);

        private readonly SensorsStorage _storage;
        protected readonly PrototypesCollection _prototype;


        internal CollectorStatusSensor StatusSensor { get; private set; }

        internal CollectorErrorsSensor CollectorErrors { get; private set; }


        protected abstract bool IsCorrectOs { get; }


        protected DefaultSensorsCollection(SensorsStorage storage, PrototypesCollection prototype)
        {
            _prototype = prototype;
            _storage = storage;
        }


        protected DefaultSensorsCollection AddCollectorAliveCommon(CollectorMonitoringInfoOptions options)
        {
            return Register(new CollectorAlive(_prototype.CollectorAlive.Get(options)));
        }

        protected DefaultSensorsCollection AddCollectorErrorsCommon()
        {
            if (CollectorErrors != null)
                return this;

            CollectorErrors = new CollectorErrorsSensor(_prototype.CollectorErrors.Get(null));

            _storage.Logger.ThrowNewError += CollectorErrors.SendCollectorError;

            return Register(CollectorErrors);
        }

        protected DefaultSensorsCollection AddCollectorVersionCommon()
        {
            return Register(new ProductVersionSensor(_prototype.CollectorVersion.Get(null)));
        }

        protected DefaultSensorsCollection AddFullCollectorMonitoringCommon(CollectorMonitoringInfoOptions monitoringOptions) =>
            AddCollectorAliveCommon(monitoringOptions).AddCollectorVersionCommon().AddCollectorErrorsCommon();


        protected DefaultSensorsCollection AddProductVersionCommon(VersionSensorOptions options)
        {
            return Register(new ProductVersionSensor(_prototype.ProductVersion.Get(options)));
        }


        protected DefaultSensorsCollection Register(SensorBase sensor)
        {
            if (!IsCorrectOs)
                throw _notSupportedException;

            _storage.Register(sensor);

            return this;
        }

        public void Dispose()
        {
            if (CollectorErrors != null)
                _storage.Logger.ThrowNewError -= CollectorErrors.SendCollectorError;
        }
    }
}