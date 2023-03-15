using System.Text.Json.Serialization;

namespace HSMServer.Core.Model.Policies
{
    public sealed class ExpectedUpdateIntervalPolicy : ServerPolicy
    {
        protected override SensorStatus FailStatus => SensorStatus.Warning;

        protected override string FailMessage => "Timeout";


        [JsonPropertyName("ExpectedUpdateInterval")]
        public long CustomPeriod { get; set; } // TODO: remove after migration

        public TimeInterval ExpectedUpdatePeriod { get; set; } // TODO: remove after migration


        [JsonConstructor] // TODO: remove after migration
        public ExpectedUpdateIntervalPolicy(TimeInterval expectedUpdatePeriod, long customPeriod) :
            base(new TimeIntervalModel(expectedUpdatePeriod, customPeriod))
        {
            ExpectedUpdatePeriod = expectedUpdatePeriod;
            CustomPeriod = customPeriod;
        }


        public ExpectedUpdateIntervalPolicy() : base() { }

        //[JsonConstructor] //TODO uncomment after migration and removed previos constructor
        public ExpectedUpdateIntervalPolicy(TimeIntervalModel interval) : base(interval) { }

        public ExpectedUpdateIntervalPolicy(long period) : base(new TimeIntervalModel(period)) { }
    }
}
