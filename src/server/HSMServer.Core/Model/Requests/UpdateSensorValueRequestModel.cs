using System;

namespace HSMServer.Core.Model.Requests;

public sealed record UpdateSensorValueRequestModel(Guid Id, SensorStatus Status, string Comment, string Initiator, string Value, bool IsReWrite = false)
{
    public string PropertyName => IsReWrite ? "Last value" : "New value";
    
    public string Environment => IsReWrite ? "Rewrite last value" : "Added new value";
    
    public string BuildComment(SensorStatus? status = null, string comment = null) => $"Status - {status ?? Status}; Comment - '{comment ?? Comment}'";
};