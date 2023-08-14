using System;
using System.Threading.Tasks;
using HSMDataCollector.Options;
using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class SensorBase : IDisposable
    {
        internal const string DefaultTimeFormat = "dd/MM/yyyy HH:mm:ss";

        private readonly string _nodePath;
        

        protected abstract string SensorName { get; }
        
        public string SensorPath => $"{_nodePath}/{SensorName}";
        
        
        internal event Action<SensorValueBase> ReceiveSensorValue;

        public event Action<string, Exception> ExceptionThrowing;

        
        protected SensorBase(SensorOptions options)
        {
            _nodePath = options.NodePath;
        }

        
        public void SendValue(SensorValueBase value)
        {
            value.Path = SensorPath;
            ReceiveSensorValue?.Invoke(value);
        }


        internal virtual Task<bool> Init() => Task.FromResult(true);
        
        internal virtual Task<bool> Start() => Task.FromResult(true);

        internal virtual Task Stop() => Task.CompletedTask;

        protected void ThrowException(Exception ex) => ExceptionThrowing?.Invoke(SensorPath, ex);


        public void Dispose() => Stop();
    }
}