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
            options.Path = options.CalculateSystemPath();
            _metainfo = options;
            _dataProcessor = options.DataProcessor ?? throw new ArgumentNullException(nameof(DataProcessor));
        }

        public void SendValue(SensorValueBase value)
        {
            if (value == null)
                return;

            value.Path = SensorPath;

            value.TrimLongComment();

            if (value is FileSensorValue file)
            {
                _dataProcessor.AddFile(file);
                return;
            }

            if (IsProiritySensor)
                _dataProcessor.AddPriorityData(value);
            else
                _dataProcessor.AddData(value);
        }

        internal virtual ValueTask<bool> InitAsync()
        {
            _dataProcessor.AddCommand(_metainfo.ApiRequest);

            return new ValueTask<bool>(true);
        }

        internal virtual ValueTask<bool> StartAsync() => new ValueTask<bool>(true);

        internal virtual ValueTask StopAsync() => default;

        protected void ThrowException(Exception ex)
        {
            _dataProcessor.AddException(SensorPath, ex);
            ExceptionThrowing?.Invoke(SensorPath, ex);
        }


        public void Dispose() => StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();

    }
}