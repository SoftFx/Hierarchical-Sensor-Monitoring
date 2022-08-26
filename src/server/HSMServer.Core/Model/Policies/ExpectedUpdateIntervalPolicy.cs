using System;

namespace HSMServer.Core.Model
{
    public enum Interval : byte
    {
        TenMinutes,
        Hour,
        Day,
        Week,
        Month,
        Custom = byte.MaxValue,
    }


    public sealed class ExpectedUpdateIntervalPolicy : Policy
    {
        private const string SensorValueOutdated = "Sensor value is older than ExpectedUpdateInterval!";

        internal static ValidationResult OutdatedSensor { get; } = new(SensorValueOutdated, SensorStatus.Warning);


        public byte ExpectedTimeInterval { get; set; }

        public long ExpectedUpdateInterval { get; set; }


        public ExpectedUpdateIntervalPolicy(long expectedUpdateInterval,
                                            byte expectedTimeInterval = (byte)Interval.Custom) : base()
        {
            Update(expectedUpdateInterval, expectedTimeInterval);
        }


        internal void Update(long expectedUpdateInterval, byte expectedTimeInterval)
        {
            ExpectedTimeInterval = expectedTimeInterval;
            ExpectedUpdateInterval = expectedUpdateInterval;
        }

        internal ValidationResult Validate(BaseValue value)
        {
            var isSensorOutdated = (Interval)ExpectedTimeInterval switch
            {
                Interval.TenMinutes => DateTime.UtcNow > value.ReceivingTime.AddMinutes(10),
                Interval.Hour => DateTime.UtcNow > value.ReceivingTime.AddHours(1),
                Interval.Day => DateTime.UtcNow > value.ReceivingTime.AddDays(1),
                Interval.Week => DateTime.UtcNow > value.ReceivingTime.AddDays(7),
                Interval.Month => DateTime.UtcNow > value.ReceivingTime.AddMonths(1),
                Interval.Custom => (DateTime.UtcNow - value.ReceivingTime).Ticks > ExpectedUpdateInterval,
                _ => throw new NotImplementedException(),
            };

            return isSensorOutdated ? OutdatedSensor : ValidationResult.Ok;
        }
    }
}
