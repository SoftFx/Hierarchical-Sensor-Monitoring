using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HSMSensorDataObjects.SensorValueRequests;

namespace HSMServer.Core.Cache
{
    /// <summary>
    /// Values the server derives itself for ordinary sensors of a user's product: the feed sensors of table sensors
    /// (#1847). No access key is involved, so none is created or needed; that is also why this is not a member of
    /// <see cref="ITreeValuesCache"/>, which the controllers see: only the server's own services are given it.
    /// <para>Narrower than a value sent with a key: a value is added only to a sensor that exists already (on its path or,
    /// after a move, through its alias), has the value's type and is not a table sensor. No sensor is created here (the
    /// caller registers it as any sensor is registered, see <see cref="ITreeValuesCache.AddOrUpdateSensorAsync"/>). A value
    /// that is added goes the way of every value: the product's queue, the path checks, the sensor's own checks (a
    /// blocked sensor takes nothing), the history store and the alerts.</para>
    /// </summary>
    public interface IServerSensorValues
    {
        /// <summary>The answer for a value whose path has no sensor: the caller may register the sensor and send it again.</summary>
        public const string NoSensor = "No sensor on this path; values of the server create none.";

        /// <summary>
        /// Adds the values to the sensors of root product <paramref name="productId"/>, on its queue. The answer has a
        /// line per value that was not added (path, reason); <see cref="NoSensor"/> for a path without a sensor.
        /// </summary>
        Task<Dictionary<string, string>> AddServerValuesAsync(Guid productId, IReadOnlyList<SensorValueBase> values, CancellationToken token = default);
    }
}
