using System;
using System.Collections.Generic;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Core.SensorsUpdatesQueue;


namespace HSMServer.Core.Model.Requests
{
    public sealed record AddSensorValuesRequest : IUpdateRequest
    {
        public IEnumerable<SensorValueBase> Values { get; init; }

        public Dictionary<string, string> Response { get; init; }

        public Guid Key { get; init; }

        public Guid ProductId { get; init; }

        public AddSensorValuesRequest(Guid key, Guid productId, IEnumerable<SensorValueBase> values)
        {
            Key = key;
            ProductId = productId;
            Values = values;
            Response = new Dictionary<string, string>();
        }

        public override string ToString()
        {
            return $"Key = {Key}, ProductId = {ProductId}, Count = { (Values is List<SensorValueBase> list ? list.Count : string.Empty) }";
        }
    }
}