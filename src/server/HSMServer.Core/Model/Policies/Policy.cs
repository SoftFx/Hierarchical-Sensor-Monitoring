using HSMDatabase.AccessManager.DatabaseEntities;
using Newtonsoft.Json;
using System;

namespace HSMServer.Core.Model.Policies
{
    public abstract class Policy
    {
        protected readonly ValidationResult _validationFail;

        protected static ValidationResult Ok => ValidationResult.Ok;


        [JsonIgnore]
        protected abstract SensorStatus FailStatus { get; }

        [JsonIgnore]
        protected abstract string FailMessage { get; }



        public Guid Id { get; init; }

        public string Type { get; init; }


        protected Policy()
        {
            _validationFail = new(FailMessage, FailStatus);

            Id = Guid.NewGuid();
            Type = GetType().Name;
        }


        internal PolicyEntity ToEntity() =>
            new()
            {
                Id = Id.ToString(),
                Policy = this,
            };
    }


    public abstract class DataPolicy<T> : Policy where T : BaseValue
    {
        internal abstract ValidationResult Validate(T value);
    }


    public abstract class ServerPolicy : Policy
    {
        public TimeIntervalModel TimeInterval { get; set; }


        public ServerPolicy() : base() { } //for serialization

        protected ServerPolicy(TimeIntervalModel interval) : base()
        {
            TimeInterval = interval;
        }


        internal virtual void Update(TimeIntervalModel interval)
        {
            TimeInterval = interval;
        }


        internal virtual ValidationResult Validate(DateTime time)
        {
            return TimeInterval.TimeIsUp(time) ? _validationFail : ValidationResult.Ok;
        }
    }
}
