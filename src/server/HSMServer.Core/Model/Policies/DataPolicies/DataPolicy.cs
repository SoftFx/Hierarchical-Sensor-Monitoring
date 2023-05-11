using HSMServer.Core.Cache.UpdateEntities;

namespace HSMServer.Core.Model.Policies
{
    public enum Operation : byte
    {
        LessThanOrEqual,
        LessThan,
        GreaterThan,
        GreaterThanOrEqual,
        Equal,
        NotEqual,
    }

    public enum TargetType : byte
    {
        Const,
        Sensor,
    }


    public sealed record TargetValue(TargetType Type, string Value);


    public class DataPolicy<T> : Policy where T : BaseValue
    {
        protected override SensorStatus FailStatus => Status;

        protected override string FailMessage => Comment;


        public string Property { get; set; }

        public Operation Action { get; set; }

        public TargetValue Target { get; set; }

        public SensorStatus Status { get; set; }

        public string Comment { get; set; }


        public DataPolicy() : base() { }


        internal DataPolicy<T> Update(DataPolicyUpdate update)
        {
            Property = update.Property;
            Action = update.Action;
            Target = update.Target;
            Status = update.Status;
            Comment = update.Comment;

            return this;
        }

        internal PolicyResult Validate(T value)
        {
            return PolicyResult.Ok;
        }
    }


    public class IntegerDataPolicy : DataPolicy<IntegerValue> { }
}
