using HSMServer.Core.TableOfChanges;
using System;

namespace HSMServer.Core.Model.Requests;

public sealed record UpdateSensorValueRequestModel
{
    public required Guid Id { get; init; }


    public InitiatorInfo Initiator { get; init; } = InitiatorInfo.System;

    public SensorStatus Status { get; init; }


    public string Comment { get; init; }

    public string Value { get; init; }

    public bool ChangeLast { get; init; }


    public string PropertyName => ChangeLast ? "Last value" : "Value";

    public string Environment => ChangeLast ? "Change last value" : "Added new value";


    public string BuildComment(SensorStatus? status = null, string comment = null, string value = null) =>
        $"Status - {status ?? Status}; Comment - '{comment ?? Comment}; Value - '{value ?? Value}''";


    public BaseValue BuildNewValue(BaseValue value, BaseValue oldValue)
    {
        value = value with
        {
            Status = Status,
            Comment = Comment,
        };

        return (ChangeLast ? SetLastValueTime(value, oldValue) : SetUtcNowTime(value)).TrySetValue(Value);
    }


    private static BaseValue SetLastValueTime(BaseValue value, BaseValue oldValue) =>
        value with
        {
            ReceivingTime = oldValue.ReceivingTime,
            Time = oldValue.Time
        };

    private static BaseValue SetUtcNowTime(BaseValue value)
    {
        var time = DateTime.UtcNow;

        if (value is BarBaseValue barValue)
            value = barValue with
            {
                CloseTime = time,
                OpenTime = time,
            };

        return value with
        {
            Time = time,
            ReceivingTime = time
        };
    }
}