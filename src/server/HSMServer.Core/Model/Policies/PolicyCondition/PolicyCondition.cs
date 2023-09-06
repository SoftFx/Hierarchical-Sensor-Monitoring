using HSMCommon.Extensions;
using HSMDatabase.AccessManager.DatabaseEntities;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Core.Model.Policies
{
    public enum PolicyOperation : byte
    {
        [Display(Name = "<=")]
        LessThanOrEqual = 0,
        [Display(Name = "<")]
        LessThan = 1,
        [Display(Name = ">")]
        GreaterThan = 2,
        [Display(Name = ">=")]
        GreaterThanOrEqual = 3,
        [Display(Name = "==")]
        Equal = 4,
        [Display(Name = "!=")]
        NotEqual = 5,

        [Display(Name = "is changed")]
        IsChanged = 20,
        [Display(Name = "is 🔴 Error")]
        IsError = 21,
        [Display(Name = "is \U0001f7e2 OK")]
        IsOk = 22,
        [Display(Name = "is changed to 🔴 Error")]
        IsChangedToError = 23,
        [Display(Name = "is changed to \U0001f7e2 OK")]
        IsChangedToOk = 24,
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

        OriginalSize = 151,
    }


    public enum PolicyCombination : byte
    {
        [Display(Name = "and")]
        And,
        [Display(Name = "or")]
        Or,
    }


    public enum TargetType : byte
    {
        Const,
        LastValue,
    }


    public sealed record TargetValue(TargetType Type, string Value);


    public abstract class PolicyCondition
    {
        public abstract PolicyOperation Operation { get; set; }

        public abstract PolicyProperty Property { get; set; }

        public abstract TargetValue Target { get; set; }


        public PolicyCombination Combination { get; set; }


        internal PolicyCondition FromEntity(PolicyConditionEntity entity)
        {
            Target = new TargetValue((TargetType)entity.Target.Type, entity.Target.Value);

            Combination = (PolicyCombination)entity.Combination;
            Operation = (PolicyOperation)entity.Operation;
            Property = (PolicyProperty)entity.Property;

            return this;
        }

        internal PolicyConditionEntity ToEntity() => new()
        {
            Target = new((byte)Target.Type, Target.Value),

            Combination = (byte)Combination,
            Operation = (byte)Operation,
            Property = (byte)Property,
        };


        public override string ToString() => $"{Property} {Operation.GetDisplayName()} {Target.Value}";
    }
}