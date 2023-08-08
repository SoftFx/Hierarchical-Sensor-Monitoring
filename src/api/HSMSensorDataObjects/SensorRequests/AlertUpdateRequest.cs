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
    }


    public enum AlertProperty : byte
    {
        Status = 0,
        Comment = 1,

        Value = 20,

        Min = 101,
        Max = 102,
        Mean = 103,
        Count = 104,
        LastValue = 105,
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


        public long Sensitivity { get; set; }

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
