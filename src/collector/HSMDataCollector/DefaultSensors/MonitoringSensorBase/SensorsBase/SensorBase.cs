using System;
using System.Threading.Tasks;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class SensorBase
    {
        private protected string _nodePath;

        protected abstract string SensorName { get; }
        
        protected internal string SensorPath => $"{_nodePath}/{SensorName}";
        
        protected virtual string GetComment() => null;
        
        protected virtual SensorStatus GetStatus() => SensorStatus.Ok;
        
        internal event Action<SensorValueBase> ReceiveSensorValue;
         
        
        protected SensorBase(SensorOptions options)
        {
            _nodePath = options.NodePath;
        }

        internal virtual Task<bool> Start()
        {
            return Task.FromResult(true);
        }
        
        internal virtual void Stop()
        {
            
        }
        
        public void Dispose()
        {
            Stop();
        }
        
        protected void SendCollectedValue(SensorValueBase value) => ReceiveSensorValue?.Invoke(value);

    }
}