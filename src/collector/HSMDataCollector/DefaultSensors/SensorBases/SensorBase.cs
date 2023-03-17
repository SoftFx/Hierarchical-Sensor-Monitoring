using System;
using System.Threading.Tasks;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.DefaultSensors.SensorBases
{
    public abstract class SensorBase : IDisposable
    {
        private readonly string _nodePath;
        

        protected abstract string SensorName { get; }
        
        protected internal string SensorPath => $"{_nodePath}/{SensorName}";
        
        
        internal event Action<SensorValueBase> ReceiveSensorValue;
         
        
        protected SensorBase(SensorOptions options)
        {
            _nodePath = options.NodePath;
        }
        

        protected virtual string GetComment() => null;
        
        protected virtual SensorStatus GetStatus() => SensorStatus.Ok;

        protected virtual void SendValue(){}
        
        protected void SendCollectedValue(SensorValueBase value) => ReceiveSensorValue?.Invoke(value);
        
        internal virtual Task<bool> Start()
        {
            SendValue();
            return Task.FromResult(true);
        }
        
        internal virtual Task Stop() => Task.CompletedTask;

        
        public void Dispose()
        {
            Stop();
        }
    }
}