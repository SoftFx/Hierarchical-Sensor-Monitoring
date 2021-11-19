using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model.Sensor
{
    public class SensorValidationParameter
    {
        public ValidationParameterType ValidationType { get; set; }
        public string ValidationValue { get; set; }
        public SensorValidationParameter(ValidationParameterEntity entity)
        {
            ValidationValue = entity.ValidationValue;
            ValidationType = (ValidationParameterType)entity.ParameterType;
        }
    }
}
