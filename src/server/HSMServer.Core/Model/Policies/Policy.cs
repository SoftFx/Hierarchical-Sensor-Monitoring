using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMServer.Core.Model.Policies
{
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type", IgnoreUnrecognizedTypeDiscriminators = true)]
    [JsonDerivedType(typeof(ExpectedUpdateIntervalPolicy), 1000)]
    [JsonDerivedType(typeof(RestoreErrorPolicy), 1100)]
    [JsonDerivedType(typeof(RestoreWarningPolicy), 1101)]
    [JsonDerivedType(typeof(RestoreOffTimePolicy), 1102)]
    [JsonDerivedType(typeof(StringValueLengthPolicy), 2000)]
    [JsonDerivedType(typeof(IntegerDataPolicy), 2001)]
    [JsonDerivedType(typeof(DoubleDataPolicy), 2002)]
    [JsonDerivedType(typeof(IntegerBarDataPolicy), 2003)]
    [JsonDerivedType(typeof(DoubleBarDataPolicy), 2004)]
    [JsonDerivedType(typeof(BooleanDataPolicy), 2005)]
    [JsonDerivedType(typeof(StringDataPolicy), 2006)]
    [JsonDerivedType(typeof(TimeSpanDataPolicy), 2007)]
    [JsonDerivedType(typeof(VersionDataPolicy), 2008)]
    [JsonDerivedType(typeof(FileDataPolicy), 2009)]
    public abstract class Policy
    {
        protected static PolicyResult Ok => PolicyResult.Ok;


        public Guid Id { get; init; }


        protected Policy()
        {
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
        protected abstract SensorStatus FailStatus { get; }

        protected abstract string FailMessage { get; }

        protected virtual string FailIcon => FailStatus.ToIcon();


        internal PolicyResult Fail { get; }


        public TimeIntervalModel Interval { get; set; }


        internal bool FromParent => Interval == null || Interval?.TimeInterval == TimeInterval.FromParent;


        protected ServerPolicy() : base()
        {
            Fail = new(FailStatus, FailMessage, FailIcon);
        }

        protected ServerPolicy(TimeIntervalModel interval) : this()
        {
            Interval = interval;
        }


        internal PolicyResult Validate(DateTime time)
        {
            return Interval != null && Interval.TimeIsUp(time) ? Fail : Ok;
        }
    }
}
