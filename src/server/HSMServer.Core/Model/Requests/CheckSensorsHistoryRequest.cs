using System;
using HSMServer.Core.SensorsUpdatesQueue;


namespace HSMServer.Core.Model.Requests
{
    internal sealed record CheckSensorsHistoryRequest(Guid ProductId) : IUpdateRequest
    {
    }
}
