using HSMServer.Core.SensorsDataValidation;

namespace HSMServer.Core.Model
{
    internal sealed class StringValueLengthPolicy : Policy
    {
        public int MaxStringLength { get; init; } = StringSensorModel.DefaultMaxStringLength;


        // Parematerless constructor for deserializing policy in PolicyDeserializationConverter
        public StringValueLengthPolicy() { }


        internal override ValidationResult Validate<T>(T value)
        {
            if (value is StringValue strValue && strValue.Value.Length > MaxStringLength)
                return PredefinedValidationResults.TooLongStringSensor;

            return PredefinedValidationResults.Success;
        }
    }
}
