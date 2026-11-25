using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Core.Model;

namespace HSMServer.Core.Cache
{
    /// <summary>
    /// A value the server derives itself for a sensor of a user's product (#1847), with what the sensor is created with when
    /// its path is free: its description, its unit, and the name the journal gives the server's action.
    /// </summary>
    public sealed record ServerSensorValue(SensorValueBase Value, string Description, Unit Unit, string Initiator);


    /// <summary>
    /// Values the server derives itself for ordinary sensors of a user's product: the feed sensors of table sensors (#1847).
    /// No access key is needed or used; a product without any key gets its values too. Not a member of <see cref="ITreeValuesCache"/>, which
    /// controllers see; <see cref="ServerSensorValues"/> is the only implementation and only the server's own services are
    /// given it.
    /// <para>One operation per value, on the product's queue, for the exact path in the exact product:</para>
    /// <list type="bullet">
    /// <item>an ordinary sensor of the value's type on the path takes the value (nothing of the sensor is changed);</item>
    /// <item>a free path gets its sensor, with the type, description and unit of the value (its row written and checked
    /// as a registration's), and the sensor takes the value; new nodes on the path are ordinary nodes and get the default
    /// key every node gets;</item>
    /// <item>a path held by a sensor of another type, by a table sensor, or by the alias of a moved sensor is refused and
    /// left exactly as it is: an alias is never followed and never displaced.</item>
    /// </list>
    /// A value that is added goes the way of every value (the path checks, the sensor's checks, the history store, the
    /// alerts); a blocked sensor or a rejected value is answered as not added.
    /// </summary>
    public interface IServerSensorValues
    {
        /// <summary>
        /// Adds the values to root product <paramref name="productId"/>. The answer has a line per value that was not
        /// added (its normalized path, the reason); a value without a line was added.
        /// </summary>
        Task<Dictionary<string, string>> AddServerValuesAsync(Guid productId, IReadOnlyList<ServerSensorValue> values, CancellationToken token = default);
    }


    /// <summary>
    /// The <see cref="IServerSensorValues"/> of the server: a small adapter over the cache, so that the container that owns
    /// the cache does not track it a second time under this interface (the cache is disposed once).
    /// </summary>
    public sealed class ServerSensorValues : IServerSensorValues
    {
        private readonly ITreeValuesCache _cache;


        public ServerSensorValues(ITreeValuesCache cache)
        {
            _cache = cache;
        }


        public Task<Dictionary<string, string>> AddServerValuesAsync(Guid productId, IReadOnlyList<ServerSensorValue> values, CancellationToken token = default) =>
            _cache is TreeValuesCache cache
                ? cache.AddServerValuesAsync(productId, values, token)
                : throw new InvalidOperationException($"{nameof(ServerSensorValues)} needs the server's {nameof(TreeValuesCache)}.");
    }
}
