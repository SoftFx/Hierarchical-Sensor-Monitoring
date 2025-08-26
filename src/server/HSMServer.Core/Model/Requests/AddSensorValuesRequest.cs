using HSMServer.Core.SensorsUpdatesQueue;
using System.Collections.Generic;


namespace HSMServer.Core.Model.Requests
{
    public sealed record AddSensorValuesRequest : IUpdateRequest
    {
        public IEnumerable<AddSensorValueRequest> Requests { get; init; }

        public Dictionary<string, string> Response { get; init; }


        public AddSensorValuesRequest(IEnumerable<AddSensorValueRequest> requests)
        {
            Requests = requests;
            Response = new Dictionary<string, string>();
        }
    }
}