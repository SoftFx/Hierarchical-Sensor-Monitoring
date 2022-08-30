using HSMServer.Core.Cache.Entities;
using System;
using System.Text.Json.Serialization;

namespace HSMServer.Core.Model
{
    public sealed class ExpectedUpdateIntervalPolicy : Policy
    {
        private const string SensorValueOutdated = "Sensor value is older than ExpectedUpdateInterval!";

        internal static ValidationResult OutdatedSensor { get; } = new(SensorValueOutdated, SensorStatus.Warning);


        public TimeInterval ExpectedUpdatePeriod { get; private set; }

        [JsonPropertyName("ExpectedUpdateInterval")]
        public long CustomPeriod { get; private set; }


        public ExpectedUpdateIntervalPolicy(long customPeriod,
                                            TimeInterval expectedUpdatePeriod = TimeInterval.Custom) : base()
        {
            ExpectedUpdatePeriod = expectedUpdatePeriod;
            CustomPeriod = customPeriod;
        }


        internal void Update(ExpectedUpdateIntervalUpdate updateModel)
        {
            ExpectedUpdatePeriod = updateModel.ExpectedUpdatePeriod;
            CustomPeriod = updateModel.CustomPeriod;
        }

        internal ValidationResult Validate(BaseValue value)
        {
            var isSensorOutdated = ExpectedUpdatePeriod switch
            {
                TimeInterval.TenMinutes => DateTime.UtcNow > value.ReceivingTime.AddMinutes(10),
                TimeInterval.Hour => DateTime.UtcNow > value.ReceivingTime.AddHours(1),
                TimeInterval.Day => DateTime.UtcNow > value.ReceivingTime.AddDays(1),
                TimeInterval.Week => DateTime.UtcNow > value.ReceivingTime.AddDays(7),
                TimeInterval.Month => DateTime.UtcNow > value.ReceivingTime.AddMonths(1),
                TimeInterval.Custom => (DateTime.UtcNow - value.ReceivingTime).Ticks > CustomPeriod,
                _ => throw new NotImplementedException(),
            };

            return isSensorOutdated ? OutdatedSensor : ValidationResult.Ok;
        }
    }
}
