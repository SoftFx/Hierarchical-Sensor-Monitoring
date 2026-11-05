using System;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.TableOfChanges;


namespace HSMServer.Core.Model.Requests
{
    // The optional Initiator (#1610): the delete path stamps the request's
    // undo group onto the AlertTemplate-type initiator, so the strip's
    // per-sensor policy records link back to the delete's rollback group.
    internal sealed record RemoveTemplateFromSensorRequest(Guid SensorId, Guid TemplateId,
        InitiatorInfo Initiator = null) : IUpdateRequest;
}
