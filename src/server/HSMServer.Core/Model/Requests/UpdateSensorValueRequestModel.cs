using System;

namespace HSMServer.Core.Model.Requests;

public sealed record UpdateSensorValueRequestModel(Guid Id, SensorStatus Status, string Comment, string Initiator, string Value, bool ChangeLast = false)
{
    public string PropertyName => ChangeLast ? "Last value" : "Value";
    
    public string Environment => ChangeLast ? "Change last value" : "Added new value";
    
    public string BuildComment(SensorStatus? status = null, string comment = null, string value = null) => $"Status - {status ?? Status}; Comment - '{comment ?? Comment}; Value - '{value ?? Value}''";
};