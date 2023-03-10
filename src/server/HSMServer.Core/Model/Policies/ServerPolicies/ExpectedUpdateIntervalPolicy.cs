using System.Text.Json.Serialization;

namespace HSMServer.Core.Model.Policies
{
    public sealed class ExpectedUpdateIntervalPolicy : ServerPolicy
    {
        protected override SensorStatus FailStatus => SensorStatus.Warning;

        protected override string FailMessage => "Timeout";


        //[JsonIgnore] //need to remove
        //public TimeIntervalModel TimeInterval { get; private set; }

        public TimeInterval ExpectedUpdatePeriod { get; private set; } //??? unnessesary

        [JsonPropertyName("ExpectedUpdateInterval")]
        public long CustomPeriod { get; private set; } // ??? unnessesary


        public ExpectedUpdateIntervalPolicy(TimeIntervalModel time) : base(time)
        {
            ExpectedUpdatePeriod = time.TimeInterval;
            CustomPeriod = time.CustomPeriod;
        }


        internal override void Update(TimeIntervalModel interval)
        {
            base.Update(interval);

            ExpectedUpdatePeriod = interval.TimeInterval;
            CustomPeriod = interval.CustomPeriod;
        }

        internal bool IsEqual(TimeIntervalModel model) =>
            ExpectedUpdatePeriod == model.TimeInterval && CustomPeriod == model.CustomPeriod;
    }
}
