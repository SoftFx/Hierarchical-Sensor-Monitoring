using HSMServer.Core.DataLayer;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public abstract class BaseSensorModel<T> : BaseSensorModel where T : BaseValue
    {
        private readonly ValidationResult _badValueType =
            new($"Sensor value type is not {typeof(T).Name}", SensorStatus.Error);

        private readonly List<Policy<T>> _policies = new();


        protected override ValuesStorage<T> Storage { get; }


        internal override bool TryAddValue(BaseValue value, out BaseValue cachedValue)
        {
            var result = TryValidate(value, out var valueT);

            cachedValue = result ? Storage.AddValue(valueT) : default;

            return result;
        }

        internal override void AddValue(byte[] valueBytes)
        {
            var value = valueBytes.ConvertToSensorValue<T>();

            if (TryValidate(value, out var valueT))
                Storage.AddValueBase(valueT);
        }

        internal override List<BaseValue> ConvertValues(List<byte[]> valuesBytes) =>
            valuesBytes.Select(v => v.ConvertToSensorValue<T>()).ToList();


        internal override void AddPolicy(Policy policy)
        {
            if (policy is Policy<T> policyT)
                _policies.Add(policyT);
            else
                base.AddPolicy(policy);
        }

        protected override List<string> GetPolicyIds()
        {
            var policies = _policies.Select(p => p.Id.ToString()).ToList();

            if (ExpectedUpdateIntervalPolicy != null)
                policies.Add(ExpectedUpdateIntervalPolicy.Id.ToString());

            return policies;
        }


        private bool TryValidate(BaseValue value, out T typedValue)
        {
            ValidationResult = ValidationResult.Ok;

            if (value is T valueT)
            {
                Validate(valueT);

                typedValue = valueT;
                return true;
            }

            typedValue = default;
            ValidationResult += _badValueType;

            return false;
        }

        private void Validate(T value)
        {
            if (value.Status != SensorStatus.Ok)
                ValidationResult = new($"User data has {value.Status} status", value.Status);

            foreach (var policy in _policies)
                ValidationResult += policy.Validate(value);
        }
    }
}
