using System;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.TableOfChanges;


namespace HSMServer.Core.Model.Requests
{
    // #1409: the product TTL detach builds its full-list update ON the queue
    // thread (reading product.Policies there) — building it on the caller
    // thread would snapshot the list and silently drop a TTL policy added
    // between snapshot and execution.
    internal sealed record DetachAlertScheduleFromProductRequest(Guid ProductId, Guid ScheduleId, InitiatorInfo Initiator) : IUpdateRequest;
}
