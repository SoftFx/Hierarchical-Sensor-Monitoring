using HSMServer.Core.Model;
using MemoryPack;
using System;

namespace PerformanceBenchmarks;

// DTO для MemoryPack
[MemoryPackable]
public partial record BooleanValueDto
{
    public DateTime ReceivingTime { get; set; }
    public SensorStatus Status { get; set; }
    public string Comment { get; set; }
    public DateTime Time { get; set; }
    public bool IsTimeout { get; set; }
    public DateTime? LastReceivingTime { get; set; }
    public long AggregatedValuesCount { get; set; }
    public double? EmaValue { get; set; }
    public bool Value { get; set; }

    public static BooleanValueDto FromBooleanValue(BooleanValue value) => new()
    {
        ReceivingTime = value.ReceivingTime,
        Status = value.Status,
        Comment = value.Comment,
        Time = value.Time,
        IsTimeout = value.IsTimeout,
        LastReceivingTime = value.LastReceivingTime,
        AggregatedValuesCount = value.AggregatedValuesCount,
        EmaValue = value.EmaValue,
        Value = value.Value
    };

    public BooleanValue ToBooleanValue() => new()
    {
        ReceivingTime = ReceivingTime,
        Status = Status,
        Comment = Comment,
        Time = Time,
        IsTimeout = IsTimeout,
        LastReceivingTime = LastReceivingTime,
        AggregatedValuesCount = AggregatedValuesCount,
        EmaValue = EmaValue,
        Value = Value
    };


}
