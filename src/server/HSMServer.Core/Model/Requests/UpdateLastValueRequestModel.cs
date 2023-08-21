using System;

namespace HSMServer.Core.Model.Requests;

public sealed record UpdateLastValueRequestModel(Guid Id, SensorStatus Status, string Comment, string Initiator, string Value)
{
    public string BuildComment(SensorStatus? status = null, string comment = null) => $"Status - {status ?? Status}; Comment - '{comment ?? Comment}'";
};