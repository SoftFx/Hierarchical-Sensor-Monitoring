using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;

namespace HSMServer.Core.SensorsDataValidation
{
    public interface ISensorsDataValidator
    {
        ValidationResult ValidateValueWithoutType(SensorValueBase value, out string validationError);
        ValidationResult ValidateBoolean(bool value, string path, string productName, out string validationError);
        ValidationResult ValidateInteger(int value, string path, string productName, out string validationError);
        ValidationResult ValidateDouble(double value, string path, string productName, out string validationError);
        ValidationResult ValidateString(string value, string path, string productName, out string validationError);
        ValidationResult ValidateIntBar(int max, int min, int mean, int count, string path, string productName, out string validationError);
        ValidationResult ValidateDoubleBar(double max, double min, double mean, int count, string path, string productName,
            out string validationError);
    }
}