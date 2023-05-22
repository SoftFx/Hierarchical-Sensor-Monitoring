using HSMServer.Core.Cache.UpdateEntities;

namespace HSMServer.Core.Model.Policies
{
    public abstract class DataPolicy<T> : Policy where T : BaseValue
    {
        private SensorStatus _status;
        private string _comment;


        protected override SensorStatus FailStatus => Status;

        protected override string FailMessage => Comment;


        public SensorStatus Status
        {
            get => _status;
            set
            {
                if (_status == value)
                    return;

                _status = value;
                InitializeFail();
            }
        }

        public string Comment
        {
            get => _comment;
            set
            {
                if (_comment == value)
                    return;

                _comment = value;
                InitializeFail();
            }
        }


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

        internal abstract PolicyResult Validate(T value);
    }
}