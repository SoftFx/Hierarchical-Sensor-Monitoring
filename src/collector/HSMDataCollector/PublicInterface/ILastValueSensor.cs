namespace HSMDataCollector.PublicInterface
{
    /// <summary>
    /// Represents the sensor, which keeps the latest received value and rewrites it when a new value is
    /// received. Only the last value is sent to a server.
    /// </summary>
    /// <typeparam name="T">Sensor type. Currently bool, int, double and string are supported</typeparam>
    public interface ILastValueSensor<T> : IInstantValueSensor<T>
    {
        
    }
}