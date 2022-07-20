using HSMServer.Core.SensorsDataValidation;

namespace HSMServer.Core.Model
{
    internal sealed class StringValueLengthPolicy : Policy<StringValue>
    {
        public int MaxStringLength { get; init; } = StringSensorModel.DefaultMaxStringLength;


        // Parematerless constructor for deserializing policy in PolicyDeserializationConverter
        public StringValueLengthPolicy() { }


        internal override ValidationResult Validate(StringValue value)
        {
            if (value.Value.Length > MaxStringLength)
                return PredefinedValidationResults.TooLongStringSensor;

            return PredefinedValidationResults.Success;
        }
    }
}
