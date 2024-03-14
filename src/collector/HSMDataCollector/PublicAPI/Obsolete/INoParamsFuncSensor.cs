using System;

namespace HSMDataCollector.PublicInterface
{
    public interface INoParamsFuncSensor<T> : IBaseFuncSensor
    {
        /// <summary>
        /// Gets the function, that is invoked within the specified interval
        /// </summary>
        /// <returns><see cref="Func{TResult}"/>Currently called function.</returns>
        Func<T> GetFunc();
    }
}