using HSMServer.Core.DataLayer;
using HSMServer.Core.SensorsDataValidation;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public abstract class BaseSensorModel<T> : BaseSensorModel where T : BaseValue
    {
        private readonly ValidationResult _badValueType =
            new($"Sensor value type is not {typeof(T).Name}", SensorStatus.Error);


        protected override ValuesStorage<T> Storage { get; }


        internal override bool TryAddValue(BaseValue value, out BaseValue cachedValue)
        {
            ValidationResult = PredefinedValidationResults.Success;

            if (value is T valueT)
            {
                Validate(valueT);

                cachedValue = Storage.AddValue(valueT);
                return true;
            }
            else
                ValidationResult += _badValueType;

            cachedValue = default;
            return false;
        }

        internal override void AddValue(byte[] valueBytes)
        {
            var value = valueBytes.ConvertToSensorValue<T>();

            if (value != null && value is T valueT)
            {
                Validate(valueT);

                Storage.AddValueBase(valueT);
            }
        }

        internal override List<BaseValue> ConvertValues(List<byte[]> valuesBytes) =>
            valuesBytes.Select(v => v.ConvertToSensorValue<T>()).ToList();

        private void Validate(BaseValue value)
        {
            if (value.Status != SensorStatus.Ok)
                ValidationResult = new(value.Status);

            foreach (var policy in _policies)
                ValidationResult += policy.Validate(value);
        }
    }
}
