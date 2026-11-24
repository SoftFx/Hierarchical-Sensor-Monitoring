using System;
using HSMCommon.Model;
using HSMServer.Core.Cache.UpdateEntities;


namespace HSMServer.Core.Model.Requests
{
    public sealed record SensorAddOrUpdateRequest : BaseUpdateRequest
    {
        public SensorUpdate Update { get; init; }

        public SensorType Type { get; init; }

        // False when the client sent no type and Type is the Boolean default:
        // such a registration must not displace a moved sensor's alias (#1718).
        public bool TypeIsExplicit { get; init; }

        // The schema of a table sensor (Type == Table, ADR-0021, #1844): required for a
        // table registration, ignored for every other type. A table sensor is created with
        // it; registering an existing table sensor again needs the same schema.
        public HSMTableBlocks.TableSchema Schema { get; init; }

        // Written by the cache when it refused a table registration (#1844): another
        // schema than the registered one, a path held by a sensor of another kind, a
        // missing schema. Unlike a failed registration of any other kind, which is
        // answered Ok, AddOrUpdateSensorAsync answers this as an error: the caller must
        // learn that its rows will be refused. Unlike Fault, sending it again changes
        // nothing. Null when the registration was not refused.
        public string Refusal { get; internal set; }

        // Written by the cache while it applies the request, read by
        // TreeValuesCache.AddOrUpdateSensorAsync once the queue has answered (#1756):
        // the text of a failure of the store, which the queue handler would otherwise only
        // log. Null when the request was applied, and also when it was refused or failed
        // for a reason that is not known to pass. Like AddSensorValuesRequest.Response it
        // is the handler's way back to the caller, and is written before the queue answers.
        public string Fault { get; internal set; }

        // Set by the cache when the registration was not applied on its own queue but
        // forwarded, through an alias, to the queue of another root product (#1756): the
        // outcome of it there, which AddOrUpdateSensorAsync awaits. Null otherwise.
        internal ForwardedRegistration Forwarded { get; set; }


        public SensorAddOrUpdateRequest(Guid productId, string path) : base(productId, path)
        {
        }
    }
}
