using HSMDataCollector.Converters;
using HSMDataCollector.Options;
using HSMDataCollector.Requests;
using HSMDataCollector.SensorsMetainfo;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Threading.Tasks;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class SensorBase : IDisposable
    {
        internal const string DefaultTimeFormat = "dd/MM/yyyy HH:mm:ss";

        private readonly SensorMetainfo _metainfo;
        //private readonly string _nodePath;


        protected abstract string SensorName { get; }

        public string SensorPath => _metainfo.Path; /*$"{_nodePath}/{SensorName}";*/


        internal event Func<PriorityRequest, Task<bool>> SensorCommandRequest;

        internal event Action<SensorValueBase> ReceiveSensorValue;


        public event Action<string, Exception> ExceptionThrowing;


        protected SensorBase(SensorOptions2 options) : this(options.Metainfo) { }

        private protected SensorBase(SensorMetainfo info)
        {
            //_nodePath = info.Path;
            _metainfo = info;
        }


        public void SendValue(SensorValueBase value)
        {
            value.Path = SensorPath;
            ReceiveSensorValue?.Invoke(value);
        }


        internal virtual Task<bool> Init() => SendCommand(new PriorityRequest(_metainfo.ToApi()));

        internal virtual Task<bool> Start() => Task.FromResult(true);

        internal virtual Task Stop() => Task.CompletedTask;

        protected void ThrowException(Exception ex) => ExceptionThrowing?.Invoke(SensorPath, ex);


        public void Dispose() => Stop();


        private Task<bool> SendCommand(PriorityRequest request) => SensorCommandRequest?.Invoke(request) ?? Task.FromResult(true);
    }
}