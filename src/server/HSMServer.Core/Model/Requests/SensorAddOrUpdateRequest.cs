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
