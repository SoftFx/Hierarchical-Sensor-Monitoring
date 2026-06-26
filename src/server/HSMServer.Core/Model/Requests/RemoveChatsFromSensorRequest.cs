using System;
using System.Collections.Generic;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.TableOfChanges;


namespace HSMServer.Core.Model.Requests
{
    internal sealed record RemoveChatsFromSensorRequest(Guid SensorId, HashSet<Guid> Chats, InitiatorInfo Initiator) : IUpdateRequest;
}
