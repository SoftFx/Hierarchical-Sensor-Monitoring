using System.Text.Json.Serialization;

namespace HSMServer.Core.Model.Policies
{
    public sealed class ExpectedUpdateIntervalPolicy : ServerPolicy
    {
        public const string PolicyIcon = "⌛";


        protected override SensorStatus FailStatus => SensorStatus.Warning;

        protected override string FailMessage => string.Empty;

        protected override string FailIcon => PolicyIcon;


        public ExpectedUpdateIntervalPolicy() : base() { }

        [JsonConstructor]
        public ExpectedUpdateIntervalPolicy(TimeIntervalModel interval) : base(interval) { }

        public ExpectedUpdateIntervalPolicy(long period) : base(new TimeIntervalModel(period)) { }
    }
}
