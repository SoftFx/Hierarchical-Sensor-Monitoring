using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMDataCollector.SensorsFactory;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class SensorBase<T> : SensorBase 
    {
        protected SensorBase(SensorOptions options) : base(options) { }
        

        public void SendValue(T value, SensorStatus status = SensorStatus.Ok, string comment = "")
        {
            SendValue(GetSensorValue(value).Complete(comment, status), SensorPath);
        }

        protected static SensorValueBase GetSensorValue(T value) => SensorValuesFactory.BuildValue(value);
    }
}