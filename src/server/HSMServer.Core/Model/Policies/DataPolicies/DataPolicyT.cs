using HSMServer.Core.Cache.UpdateEntities;
using System.Text.Json.Serialization;

namespace HSMServer.Core.Model.Policies
{
    public abstract class DataPolicy : Policy
    {
        internal protected SensorResult SensorResult { get; protected set; }

        internal protected string AlertComment { get; protected set; }


        internal protected (string, string) AlertKey => (Icon, Template);


        public virtual SensorStatus Status { get; set; } = SensorStatus.Ok;

        public string Icon { get; set; }

        [JsonPropertyName("Comment")]
        public string Template { get; set; }
    }


    public abstract class DataPolicy<T> : DataPolicy where T : BaseValue
    {
        public abstract string Property { get; set; }

        public abstract PolicyOperation Operation { get; set; }

        public abstract TargetValue Target { get; set; }


        internal void Update(DataPolicyUpdate update)
        {
            Operation = update.Operation;
            Property = update.Property;
            Template = update.Template;
            Target = update.Target;
            Status = update.Status;
        }

        internal abstract bool Validate(T value, BaseSensorModel sensor);

        protected abstract string GetComment(T value, BaseSensorModel sensor);
    }
}