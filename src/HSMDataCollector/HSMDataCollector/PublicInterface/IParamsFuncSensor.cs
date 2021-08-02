using System;
using System.Collections.Generic;

namespace HSMDataCollector.PublicInterface
{
    /// <summary>
    /// The sensor invokes a Func which takes <c><list type="U"></list></c> as a parameter and returns <list type="T"></list> result
    /// </summary>
    /// <typeparam name="U">Func parameter type</typeparam>
    /// <typeparam name="T">Result value type</typeparam>
    public interface IParamsFuncSensor<T, U>
    {
        /// <summary>
        /// Gets the function, that is invoked within the specified interval
        /// </summary>
        /// <returns><see cref="Func{TResult}"/>Currently called function.</returns>
        Func<List<U>, T> GetFunc();

        /// <summary>
        /// Get current interval, within the specified function is invoked. 
        /// </summary>
        /// <returns>Current invoke interval.</returns>
        TimeSpan GetInterval();

        /// <summary>
        /// Restart the invoke timer with a new interval specified.
        /// </summary>
        /// <param name="timeSpan">New invoke interval.</param>
        void RestartTimer(TimeSpan timeSpan);
        /// <summary>
        /// Add new value to the params list.
        /// </summary>
        /// <param name="value">New value to be added.</param>
        void AddValue(U value);
    }
}