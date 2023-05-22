using HSMServer.Core.Cache.UpdateEntities;

namespace HSMServer.Core.Model.Policies
{
    public abstract class DataPolicy<T> : Policy where T : BaseValue
    {
        protected override SensorStatus FailStatus => Status;

        protected override string FailMessage { get; }


        public SensorStatus Status { get; set; }

        public string Comment { get; set; }


        public abstract string Property { get; set; }

        public abstract PolicyOperation Operation { get; set; }

        public abstract TargetValue Target { get; set; }


        internal void Update(DataPolicyUpdate update)
        {
            Operation = update.Operation;
            Property = update.Property;
            Comment = update.Comment;
            Target = update.Target;
            Status = update.Status;
        }

        internal abstract PolicyResult Validate(T value, BaseSensorModel sensor);

        protected abstract string GetComment(T value, BaseSensorModel sensor);
    }
}