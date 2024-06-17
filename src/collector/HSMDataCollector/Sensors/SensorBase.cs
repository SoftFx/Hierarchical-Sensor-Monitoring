using System;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
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

        internal readonly IDataProcessor _dataProducer;

        protected SensorBase(SensorOptions options)
        {
            options.Path = options.CalculateSystemPath();
            _metainfo = options;
            _dataProducer = options.DataProcessor;
        }

        public void SendValue(SensorValueBase value)
        {
            value.Path = SensorPath;
            if (IsProiritySensor)
                _dataProducer.AddData(value);
            else
                _dataProducer.AddData(value);
        }

        internal virtual Task<bool> InitAsync()
        {
            _dataProducer.AddCommand(_metainfo.ApiRequest);
            return Task.FromResult(true);
        }

        internal virtual Task<bool> StartAsync() => Task.FromResult(true);

        internal virtual Task StopAsync() => Task.CompletedTask;

        protected void ThrowException(Exception ex)
        {
            _dataProducer.AddException(SensorPath, ex);
            ExceptionThrowing?.Invoke(SensorPath, ex);
        }


        public void Dispose() => StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();

    }
}