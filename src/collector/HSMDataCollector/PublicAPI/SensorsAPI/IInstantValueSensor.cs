using HSMSensorDataObjects;

namespace HSMDataCollector.PublicInterface
{
    /// <summary>
    /// Represents the sensor, which immediately sends the data right after receiving it.
    /// </summary>
    /// <typeparam name="T">Data type of the sensor. bool, int, double and string are supported.</typeparam>
    public interface IInstantValueSensor<T>
    {
        /// <summary>
        /// Adds instant sensor value
        /// </summary>
        /// <param name="value">Instant value</param>
        void AddValue(T value);

        /// <summary>
        /// Adds instant sensor value and the comment
        /// </summary>
        /// <param name="value">Instant value</param>
        /// <param name="comment">Comment to the value; will be displayed in the values table. Defaults to ""</param>
        void AddValue(T value, string comment = "");

        /// <summary>
        /// Adds instant value, comment and status for the sensor
        /// </summary>
        /// <param name="value">Instant value</param>
        /// <param name="status">Sensor status (type <see cref="SensorStatus"/>) which will be applied to the sensor
        /// in the tree and used for notifications. Defaults to SensorStatus.Ok</param>
        /// <param name="comment">Comment to the value; will be displayed in the values table. Defaults to ""</param>
        void AddValue(T value, SensorStatus status = SensorStatus.Ok, string comment = "");
    }
}