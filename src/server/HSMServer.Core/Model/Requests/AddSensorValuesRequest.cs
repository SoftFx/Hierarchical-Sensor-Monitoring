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

        public string ProductName { get; init; }

        public AddSensorValuesRequest(Guid key, string productName, IEnumerable<SensorValueBase> values)
        {
            Key = key;
            ProductName = productName;
            Values = values;
            Response = new Dictionary<string, string>();
        }
    }
}