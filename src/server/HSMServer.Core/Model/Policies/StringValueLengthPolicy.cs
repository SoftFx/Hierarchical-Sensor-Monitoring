using HSMServer.Core.SensorsDataValidation;

namespace HSMServer.Core.Model
{
    internal sealed class StringValueLengthPolicy : Policy<StringValue>
    {
        private const string SensorValueIsTooLong = "The value has exceeded the length limit.";

        private readonly ValidationResult _tooLongStringSensor = new(SensorValueIsTooLong, SensorStatus.Warning);


        public int MaxStringLength { get; init; } = StringSensorModel.DefaultMaxStringLength;


        // Parematerless constructor for deserializing policy in PolicyDeserializationConverter
        public StringValueLengthPolicy() { }


        internal override ValidationResult Validate(StringValue value)
        {
            if (value.Value.Length > MaxStringLength)
                return _tooLongStringSensor;

            return ValidationResult.Ok;
        }
    }
}
