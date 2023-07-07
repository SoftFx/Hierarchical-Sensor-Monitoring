using HSMDatabase.AccessManager.DatabaseEntities;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Core.Model.Policies
{
    public enum PolicyOperation : byte
    {
        [Display(Name = "<=")]
        LessThanOrEqual,
        [Display(Name = "<")]
        LessThan,
        [Display(Name = ">")]
        GreaterThan,
        [Display(Name = ">=")]
        GreaterThanOrEqual,
        [Display(Name = "==")]
        Equal,
        [Display(Name = "!=")]
        NotEqual,
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
        Sensor,
    }


    public sealed record TargetValue(TargetType Type, string Value);


    public abstract class PolicyCondition
    {
        public abstract PolicyOperation Operation { get; set; }

        public abstract TargetValue Target { get; set; }

        public abstract string Property { get; set; }


        public PolicyCombination Combination { get; set; }


        internal PolicyCondition FromEntity(PolicyConditionEntity entity)
        {
            Target = new TargetValue((TargetType)entity.Target.Type, entity.Target.Value);

            Combination = (PolicyCombination)entity.Combination;
            Operation = (PolicyOperation)entity.Operation;

            Property = entity.Property;

            return this;
        }

        internal PolicyConditionEntity ToEntity() => new()
        {
            Target = new((byte)Target.Type, Target.Value),

            Combination = (byte)Combination,
            Operation = (byte)Operation,

            Property = Property,
        };
    }
}