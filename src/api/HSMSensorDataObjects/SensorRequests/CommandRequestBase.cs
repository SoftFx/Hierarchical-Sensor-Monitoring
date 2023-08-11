namespace HSMSensorDataObjects.SensorRequests
{
    public enum Command
    {
        AddOrUpdateSensor,
    }


    public abstract class CommandRequestBase : BaseRequest
    {
        public abstract Command Type { get; }
    }
}
