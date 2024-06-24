using System;
using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors.Diagnostic;
using HSMDataCollector.DefaultSensors.Other;
using HSMDataCollector.Options;


namespace HSMDataCollector.DefaultSensors
{
    internal abstract class DefaultSensorsCollection : IDisposable
    {
        private const string NotSupportedSensor = "Sensor is not supported for current OS";

        private static readonly NotSupportedException _notSupportedException = new NotSupportedException(NotSupportedSensor);

        private readonly SensorsStorage _storage;
        protected readonly PrototypesCollection _prototype;


        internal PackageDataAvrProcessTimeSensor PackageProcessTimeSensor { get; private set; }
        internal PackageDataCountSensor PackageDataCountSensor { get; private set; }
        internal PackageContentSizeSensor PackageSizeSensor { get; private set; }

        internal QueueOverflowSensor QueueOverflowSensor { get; private set; }


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
            if (QueueOverflowSensor != null)
                return this;

            QueueOverflowSensor = new QueueOverflowSensor(_prototype.QueueOverflow.Get(options));

            return Register(QueueOverflowSensor);
        }


        protected DefaultSensorsCollection AddPackageValuesCountCommon(BarSensorOptions options)
        {
            if (PackageDataCountSensor != null)
                return this;

            PackageDataCountSensor = new PackageDataCountSensor(_prototype.PackageValuesCount.Get(options));

           // _storage.QueueManager.PackageInfoEvent += _packageDataCountSensor.AddValue;

            return Register(PackageDataCountSensor);
        }


        protected DefaultSensorsCollection AddPackageContentSizeCommon(BarSensorOptions options)
        {
            if (PackageSizeSensor != null)
                return this;

            PackageSizeSensor = new PackageContentSizeSensor(_prototype.PackageContentSize.Get(options));

           // _storage.QueueManager.PackageRequestInfoEvent += _packageSizeSensor.AddValue;

            return Register(PackageSizeSensor);
        }


        protected DefaultSensorsCollection AddPackageProcessTimeCommon(BarSensorOptions options)
        {
            if (PackageProcessTimeSensor != null)
                return this;

            PackageProcessTimeSensor = new PackageDataAvrProcessTimeSensor(_prototype.PackageProcessTime.Get(options));

            //_storage.QueueManager.PackageInfoEvent += _packageProcessTimeSensor.AddValue;

            return Register(PackageProcessTimeSensor);
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
            //if (_packageProcessTimeSensor != null)
            //    _storage.QueueManager.PackageInfoEvent -= _packageProcessTimeSensor.AddValue;

            //if (_packageDataCountSensor != null)
            //    _storage.QueueManager.PackageInfoEvent -= _packageDataCountSensor.AddValue;

            //if (_queueOverflowSensor != null)
            //    _storage.QueueManager.OverflowInfoEvent -= _queueOverflowSensor.AddValue;

            //if (CollectorErrors != null)
            //    _storage.Logger.ThrowNewError -= CollectorErrors.SendCollectorError;
        }
    }
}