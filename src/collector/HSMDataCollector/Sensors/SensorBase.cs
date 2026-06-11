using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.DefaultSensors
{

    public abstract class SensorBase<TDisplayUnit> : ISensor, ISensorIdentity where TDisplayUnit : struct, Enum
    {
        internal const string DefaultTimeFormat = "dd/MM/yyyy HH:mm:ss";

        private readonly SensorOptions<TDisplayUnit> _metainfo;

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
                _dataProcessor.AddCommand(this, _metainfo.ApiRequest);

                return new ValueTask<bool>(true);
            }
            catch (Exception ex)
            {
                HandleException(ex);

                return new ValueTask<bool>(false);
            }
        }

        public virtual ValueTask<bool> StartAsync() => new ValueTask<bool>(true);

        public virtual ValueTask StopAsync() => default;

        protected virtual ValueTask DisposeAsyncCore() => StopAsync();

        protected void HandleException(Exception ex)
        {
            try
            {
                _dataProcessor?.AddException(SensorPath, ex);
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
                        _dataProcessor?.AddException(SensorPath, handlerEx);
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
