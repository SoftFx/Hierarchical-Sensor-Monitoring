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
        private readonly List<DataPolicy<T>> _dataPolicies = new();
        private readonly CorrectDataTypePolicy<T> _typePolicy = new();

        protected override ValuesStorage<T> Storage { get; }


        protected BaseSensorModel(SensorEntity entity) : base(entity)
        {
            _dataPolicies.Add(_typePolicy);
        }


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


        internal override void AddPolicy<U>(U policy)
        {
            if (policy is DataPolicy<T> policyT)
                _dataPolicies.Add(policyT);
            else
                base.AddPolicy(policy);
        }

        protected override List<Guid> GetPolicyIds()
        {
            var policies = base.GetPolicyIds();

            policies.AddRange(_dataPolicies.Where(u => u != _typePolicy)
                                           .Select(u => u.Id));

            return policies;
        }


        private bool TryValidate(BaseValue value, out T valueT)
        {
            _dataResult = ValidationResult.Ok;

            valueT = value as T;

            foreach (var policy in _dataPolicies)
            {
                _dataResult += policy.Validate(valueT);

                if (policy == _typePolicy && !_dataResult.IsOk)
                    return false;
            }

            _dataResult += ValidationResult.FromValue(valueT); //add user status

            return true;
        }
    }
}
