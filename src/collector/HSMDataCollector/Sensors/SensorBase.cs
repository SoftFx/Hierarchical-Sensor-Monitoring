using System;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.DefaultSensors
{
    public abstract class SensorBase : IDisposable
    {
        internal const string DefaultTimeFormat = "dd/MM/yyyy HH:mm:ss";

        private readonly SensorOptions _metainfo;

        public string SensorPath => _metainfo.Path;

        internal bool IsProiritySensor => _metainfo.IsPrioritySensor;

        public event Action<string, Exception> ExceptionThrowing;

        internal readonly DataProcessor _dataProcessor;

        protected SensorBase(SensorOptions options)
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

        internal virtual ValueTask<bool> InitAsync()
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

        internal virtual ValueTask<bool> StartAsync() => new ValueTask<bool>(true);

        internal virtual ValueTask StopAsync() => default;

        protected void HandleException(Exception ex)
        {
            _dataProcessor?.AddException(SensorPath, ex);
            ExceptionThrowing?.Invoke(SensorPath, ex);
        }


        public void Dispose() => StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();

    }
}