using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMServer.Core.Model.Policies
{
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [JsonDerivedType(typeof(ExpectedUpdateIntervalPolicy), 1000)]
    [JsonDerivedType(typeof(RestoreErrorPolicy), 1100)]
    [JsonDerivedType(typeof(RestoreWarningPolicy), 1101)]
    [JsonDerivedType(typeof(RestoreOffTimePolicy), 1102)]
    [JsonDerivedType(typeof(StringValueLengthPolicy), 2000)]
    [JsonDerivedType(typeof(IntegerDataPolicy), 2001)]
    [JsonDerivedType(typeof(DoubleBarDataPolicy), 2004)]
    public abstract class Policy
    {
        protected static PolicyResult Ok => PolicyResult.Ok;


        protected abstract SensorStatus FailStatus { get; }

        protected abstract string FailMessage { get; }

        protected virtual string FailIcon => FailStatus.ToIcon();


        internal PolicyResult Fail { get; }


        public Guid Id { get; init; }


        protected Policy()
        {
            Fail = new(FailStatus, FailMessage, FailIcon);

            Id = Guid.NewGuid();
        }


        internal PolicyEntity ToEntity() =>
            new()
            {
                Id = Id.ToString(),
                Policy = JsonSerializer.SerializeToUtf8Bytes(this),
            };
    }


    public abstract class ServerPolicy : Policy
    {
        public TimeIntervalModel Interval { get; set; }


        internal bool FromParent => Interval == null || Interval?.TimeInterval == TimeInterval.FromParent;


        protected ServerPolicy() : base() { }

        protected ServerPolicy(TimeIntervalModel interval) : base()
        {
            Interval = interval;
        }


        internal PolicyResult Validate(DateTime time)
        {
            return Interval != null && Interval.TimeIsUp(time) ? Fail : Ok;
        }
    }
}
