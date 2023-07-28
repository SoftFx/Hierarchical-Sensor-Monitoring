using System.Collections.Generic;

namespace HSMDataCollector.PublicInterface
{
    /// <summary>
    /// Represents a sensor, which collects the data within the specified period and passes to the server
    /// some numeric characteristics like max, min, mean, etc.
    /// </summary>
    /// <typeparam name="T">The type of the data. int and double are currently supported</typeparam>
    public interface IBarSensor<T> where T : struct
    {
        /// <summary>
        /// Adds the value to the bar list
        /// </summary>
        /// <param name="value">The value to be added</param>
        void AddValue(T value);

        /// <summary>
        /// Adds the bunch of values to the bar list
        /// </summary>
        /// <param name="value">The bunch of values to be added</param>
        void AddValues(IEnumerable<T> values);
    }
}