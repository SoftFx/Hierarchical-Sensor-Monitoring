using System.Collections.Generic;

namespace HSMSensorDataObjects.SensorUpdateRequests
{
    public enum PolicyOperation : byte
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

    public enum PolicyProperty : byte
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

    public enum PolicyCombination : byte
    {
        And,
        Or,
    }


    public sealed class PolicyUpdateRequest
    {
        //Guid Id,
        public List<PolicyConditionUpdate> Conditions { get; set; }

        public SensorStatus Status { get; set; }

        public long Sensitivity { get; set; }

        public string Template { get; set; }

        public string Icon { get; set; }
    }


    public sealed class PolicyConditionUpdate
    {
        public PolicyOperation Operation { get; set; }

        public PolicyProperty Property { get; set; }

        public TargetValue Target { get; set; }

        public PolicyCombination Combination { get; set; }
    }


    public sealed class TargetValue
    {
        public TargetType Type { get; set; }
        public string Value { get; set; }
    }
}
