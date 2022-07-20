using System;

namespace HSMServer.Core.Model
{
    public sealed class ExpectedUpdateIntervalPolicy : Policy
    {
        private const string SensorValueOutdated = "Sensor value is older than ExpectedUpdateInterval!";

        private readonly ValidationResult _outdatedSensor = new(SensorValueOutdated, SensorStatus.Warning);


        public long ExpectedUpdateInterval { get; init; }


        public ExpectedUpdateIntervalPolicy(long expectedUpdateInterval) : base()
        {
            ExpectedUpdateInterval = expectedUpdateInterval;
        }


        internal ValidationResult Validate(BaseValue value)
        {
            if ((DateTime.UtcNow - value.ReceivingTime).Ticks > ExpectedUpdateInterval)
                return _outdatedSensor;

            return ValidationResult.Ok;
        }
    }
}
