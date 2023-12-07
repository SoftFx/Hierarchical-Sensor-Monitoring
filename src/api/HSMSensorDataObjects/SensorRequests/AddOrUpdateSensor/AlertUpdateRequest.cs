using System.Collections.Generic;

namespace HSMSensorDataObjects.SensorRequests
{
    public enum AlertOperation : byte
    {
        LessThanOrEqual = 0,
        LessThan = 1,
        GreaterThan = 2,
        GreaterThanOrEqual = 3,
        Equal = 4,
        NotEqual = 5,

        IsChanged = 20,
        IsError = 21,
        IsOk = 22,
        IsChangedToError = 23,
        IsChangedToOk = 24,

        Contains = 30,
        StartsWith = 31,
        EndsWith = 32,

        ReceivedNewValue = 50,
    }


    public enum AlertProperty : byte
    {
        Status = 0,
        Comment = 1,

        Value = 20,
        EmaValue = 21,

        Min = 101,
        Max = 102,
        Mean = 103,
        Count = 104,
        LastValue = 105,
        FirstValue = 106,

        EmaMin = 107,
        EmaMax = 108,
        EmaMean = 109,
        EmaCount = 110,

        Length = 120,

        OriginalSize = 151,

        NewSensorData = 200,
    }


    public enum TargetType : byte
    {
        Const,
        LastValue,
    }


    public enum AlertCombination : byte
    {
        And,
        Or,
    }


    public sealed class AlertUpdateRequest
    {
        public List<AlertConditionUpdate> Conditions { get; set; }

        public SensorStatus Status { get; set; }


        public string Template { get; set; }

        public string Icon { get; set; }


        public long? ConfirmationPeriod { get; set; }

        public bool IsDisabled { get; set; }
    }


    public sealed class AlertConditionUpdate
    {
        public AlertCombination Combination { get; set; }

        public AlertOperation Operation { get; set; }

        public AlertProperty Property { get; set; }

        public TargetValue Target { get; set; }
    }


    public sealed class TargetValue
    {
        public TargetType Type { get; set; }

        public string Value { get; set; }
    }
}
