using HSMDataCollector.Options;
using HSMDataCollector.Prototypes;
using HSMDataCollector.Requests;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Threading.Tasks;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class SensorBase : IDisposable
    {
        internal const string DefaultTimeFormat = "dd/MM/yyyy HH:mm:ss";

        private readonly SensorOptions _metainfo;


        public string SensorPath => _metainfo.Path;


        internal event Func<PriorityRequest, Task<bool>> SensorCommandRequest;
        internal event Action<SensorValueBase> ReceiveSensorValue;

        public event Action<string, Exception> ExceptionThrowing;


        protected SensorBase(SensorOptions options)
        {
            options.Path = DefaultPrototype.BuildPath(options.Module, options.Path);

            _metainfo = options;
        }


        public void SendValue(SensorValueBase value)
        {
            value.Path = SensorPath;
            ReceiveSensorValue?.Invoke(value);
        }


        internal virtual Task<bool> Init() => SendCommand(new PriorityRequest(_metainfo.ApiRequest));

        internal virtual Task<bool> Start() => Task.FromResult(true);

        internal virtual Task Stop() => Task.CompletedTask;

        protected void ThrowException(Exception ex) => ExceptionThrowing?.Invoke(SensorPath, ex);


        public void Dispose() => Stop();


        private Task<bool> SendCommand(PriorityRequest request) => SensorCommandRequest?.Invoke(request) ?? Task.FromResult(true);
    }
}