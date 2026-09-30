using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.DefaultSensors
{

    public abstract class SensorBase<TDisplayUnit> : ISensor, ISensorIdentity, IDescribableSensor where TDisplayUnit : struct, Enum
    {
        // ALWAYS format with CultureInfo.InvariantCulture: '/' and ':' are the date/time
        // SEPARATOR placeholders in a custom format string, not literals, so a bare
        // ToString(DefaultTimeFormat) renders "22.09.2026 18:33:37" on ru-RU. These timestamps
        // go out as wire-visible comments and must read the same on every host (#1433).
        internal const string DefaultTimeFormat = "dd/MM/yyyy HH:mm:ss";

        private readonly SensorOptions<TDisplayUnit> _metainfo;

        // Guards the registration inputs that can change after creation (the description) together
        // with building and enqueueing the AddOrUpdate request, so a registration always carries the
        // latest text and concurrent SetDescription calls enqueue in the order they changed it.
        // Under it only the lifecycle-state reads (CollectorLifecycle._lock, itself a leaf) and the
        // command-queue enqueue run (plus, when that evicts, the overflow sensor's value add) — never
        // another sensor's registration lock or the collector's lifecycle gate, so no lock cycle.
        private readonly object _registrationLock = new object();

        public string SensorPath => _metainfo.Path;
        SensorType ISensorIdentity.Type => _metainfo.Type;
        bool ISensorIdentity.IsLastValue => IsLastValue;

        protected virtual bool IsLastValue => false;

        internal bool IsProiritySensor => _metainfo.IsPrioritySensor;

        public event Action<string, Exception> ExceptionThrowing;

        internal readonly DataProcessor _dataProcessor;

        protected SensorBase(SensorOptions<TDisplayUnit> options)
        {
            options.Path   = options.CalculateSystemPath();
            _metainfo      = options;
            _dataProcessor = options.DataProcessor ?? throw new ArgumentNullException(nameof(DataProcessor));
        }

        public void SendValue(SensorValueBase value)
        {
            try
            {
                if (value == null)
                    return;

                value.Path = SensorPath;

                // The public Time setter accepts DateTimeKind.Local, which serializes with a
                // machine-local offset and shifts timestamp interpretation (#1102-E5). Normalize at
                // the send boundary; the wire DTO stays untouched.
                if (value.Time.Kind == DateTimeKind.Local)
                    value.Time = value.Time.ToUniversalTime();

                value.TrimLongComment();

                if (value is FileSensorValue file)
                {
                _dataProcessor.AddFile(this, file);
                    return;
                }

                if (IsProiritySensor)
                    _dataProcessor.AddPriorityData(this, value);
                else
                    _dataProcessor.AddData(this, value);
            }
            catch (Exception ex) 
            {
                HandleException(ex);
            }
        }

        public virtual ValueTask<bool> InitAsync()
        {
            try
            {
                lock (_registrationLock)
                    _dataProcessor.AddCommand(this, _metainfo.ApiRequest);

                return new ValueTask<bool>(true);
            }
            catch (Exception ex)
            {
                HandleException(ex);

                return new ValueTask<bool>(false);
            }
        }

        // Mirrors the native hsm_sensor_set_description (#1482): the options keep the new text, so the
        // next Start's InitAsync registers it; while Starting/Running the AddOrUpdate is queued now.
        /// <inheritdoc/>
        public bool SetDescription(string description)
        {
            try
            {
                lock (_registrationLock)
                {
                    _metainfo.Description = description;

                    // Same gate as a sensor created at runtime (SensorsStorage.Register): Starting or
                    // Running re-registers now; Stopped leaves it to the next Start; Stopping and
                    // Disposed send nothing.
                    if (_dataProcessor.CanStartNewSensors)
                        _dataProcessor.AddCommand(this, _metainfo.ApiRequest);
                }

                return true;
            }
            catch (Exception ex)
            {
                HandleException(ex);

                return false;
            }
        }

        public virtual ValueTask<bool> StartAsync() => new ValueTask<bool>(true);

        public virtual ValueTask StopAsync() => default;

        protected virtual ValueTask DisposeAsyncCore() => StopAsync();

        protected void HandleException(Exception ex)
        {
            // _dataProcessor is non-null by the ctor invariant; the try/catch is not a null guard
            // but isolation against AddException itself failing.
            try
            {
                _dataProcessor.AddException(SensorPath, ex);
            }
            catch (Exception reportEx)
            {
                Trace.TraceError($"Sensor {SensorPath} failed to report an exception: {reportEx}");
            }

            var subscribers = ExceptionThrowing;
            if (subscribers == null)
                return;

            // HandleException is the scheduler's onError callback for monitoring sensors, so this
            // event fires inside async-void dispatch where an escaping exception kills the host
            // process (#1102-A1). Isolate per subscriber, same policy as the lifecycle events.
            foreach (Action<string, Exception> handler in subscribers.GetInvocationList())
            {
                try
                {
                    handler(SensorPath, ex);
                }
                catch (Exception handlerEx)
                {
                    try
                    {
                        _dataProcessor.AddException(SensorPath, handlerEx);
                    }
                    catch (Exception reportEx)
                    {
                        Trace.TraceError($"Sensor {SensorPath} failed to report an {nameof(ExceptionThrowing)} handler error: {reportEx}");
                    }
                }
            }
        }


        public void Dispose() => DisposeAsyncCore().ConfigureAwait(false).GetAwaiter().GetResult();

    }
}
