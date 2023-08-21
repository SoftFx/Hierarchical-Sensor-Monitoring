using System;

namespace HSMServer.Core.Model.Requests;

public sealed record UpdateSensorValueRequestModel(Guid Id, SensorStatus Status, string Comment, string Initiator, string Value, bool IsReWrite = false)
{
    public string PropertyName => IsReWrite ? "Last value" : "Value";
    
    public string Environment => IsReWrite ? "Change last value" : "Added new value";
    
    public string BuildComment(SensorStatus? status = null, string comment = null, string value = null) => $"Status - {status ?? Status}; Comment - '{comment ?? Comment}; Value - '{value ?? Value}''";
};