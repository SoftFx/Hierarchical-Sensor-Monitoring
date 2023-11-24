using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors.Diagnostic;
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


        private PackageContentSizeSensor _packageSizeCommon;
        private PackageDataCountSensor _packageDataCountSensor;
        private QueueOverflowSensor _queueOverflowSensor;


        internal CollectorStatusSensor StatusSensor { get; private set; }

        internal CollectorErrorsSensor CollectorErrors { get; private set; }


        protected abstract bool IsCorrectOs { get; }


        protected DefaultSensorsCollection(SensorsStorage storage, PrototypesCollection prototype)
        {
            _prototype = prototype;
            _storage = storage;
        }


        #region Collector sensors

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

        #endregion

        #region Diagnostic sensors

        protected DefaultSensorsCollection AddQueueOverflowCommon(BarSensorOptions options)
        {
            if (_queueOverflowSensor != null)
                return this;

            _queueOverflowSensor = new QueueOverflowSensor(_prototype.QueueOverflow.Get(options));

            _storage.QueueManager.OverflowInfo += _queueOverflowSensor.AddValue;

            return Register(_queueOverflowSensor);
        }


        protected DefaultSensorsCollection AddPackageValuesCountCommon(BarSensorOptions options)
        {
            if (_packageDataCountSensor != null)
                return this;

            _packageDataCountSensor = new PackageDataCountSensor(_prototype.PackageValuesCount.Get(options));

            _storage.QueueManager.PackageValuesCountInfo += _packageDataCountSensor.AddValue;

            return Register(_packageDataCountSensor);
        }


        protected DefaultSensorsCollection AddPackageSizeCommon(BarSensorOptions options)
        {
            if (_packageSizeCommon != null)
                return this;

            _packageSizeCommon = new PackageContentSizeSensor(_prototype.PackageContentSize.Get(options));

            _storage.QueueManager.PackageSendingInfo += _packageSizeCommon.AddValue;

            return Register(_packageSizeCommon);
        }

        #endregion



        protected DefaultSensorsCollection Register(SensorBase sensor)
        {
            if (!IsCorrectOs)
                throw _notSupportedException;

            _storage.Register(sensor);

            return this;
        }

        public void Dispose()
        {
            if (_queueOverflowSensor != null)
                _storage.QueueManager.OverflowInfo -= _queueOverflowSensor.AddValue;

            if (_packageDataCountSensor != null)
                _storage.QueueManager.PackageValuesCountInfo -= _packageDataCountSensor.AddValue;

            if (CollectorErrors != null)
                _storage.Logger.ThrowNewError -= CollectorErrors.SendCollectorError;
        }
    }
}