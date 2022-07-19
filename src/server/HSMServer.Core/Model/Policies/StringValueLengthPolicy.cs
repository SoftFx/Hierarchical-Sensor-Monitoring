using HSMServer.Core.SensorsDataValidation;

namespace HSMServer.Core.Model
{
    internal sealed class StringValueLengthPolicy : Policy
    {
        private const int MaxStringLength = 150;


        internal override ValidationResult Validate<T>(T value)
        {
            if (value is StringValue strValue && strValue.Value.Length > MaxStringLength)
                return PredefinedValidationResults.TooLongStringSensor;

            return PredefinedValidationResults.Success;
        }
    }
}
