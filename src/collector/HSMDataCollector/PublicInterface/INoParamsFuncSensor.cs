using System;

namespace HSMDataCollector.PublicInterface
{
    public interface INoParamsFuncSensor<T>
    {
        /// <summary>
        /// Gets the function, that is invoked within the specified interval
        /// </summary>
        /// <returns><see cref="Func{TResult}"/>Currently called function.</returns>
        Func<T> GetFunc();

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
    }
}
