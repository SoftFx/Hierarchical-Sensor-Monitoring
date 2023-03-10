using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public abstract class BaseSensorModel<T> : BaseSensorModel where T : BaseValue
    {
        private readonly ValidationResult _badValueType =
            new($"Sensor value type is not {typeof(T).Name}", SensorStatus.Error);

        private readonly List<DataPolicy<T>> _dataPolicies = new();


        protected BaseSensorModel(SensorEntity entity) : base(entity) { }


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
            if (policy is DataPolicy<T> policyT)
                _dataPolicies.Add(policyT);
            else
                base.AddPolicy(policy);
        }

        protected override List<Guid> GetPolicyIds()
        {
            var policies = base.GetPolicyIds();

            policies.AddRange(_dataPolicies.Select(u => u.Id));

            return policies;
        }


        private bool TryValidate(BaseValue value, out T typedValue)
        {
            _dataResult = ValidationResult.Ok;

            if (value is T valueT)
            {
                Validate(valueT);

                typedValue = valueT;
                return true;
            }

            typedValue = default;
            _dataResult += _badValueType;

            return false;
        }

        private void Validate(T value)
        {
            if (value.Status != SensorStatus.Ok)
            {
                var message = string.IsNullOrEmpty(value.Comment) ? $"User data has {value.Status} status" : value.Comment;
                _dataResult = new(message, value.Status);
            }

            foreach (var policy in _dataPolicies)
                _dataResult += policy.Validate(value);
        }
    }
}
