using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Extensions;
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
            _dataPolicies.Add(_typePolicy); //alwayes should be first in list
        }


        internal override bool TryAddValue(BaseValue value)
        {
            var canStore = TryValidate(value, out var valueT);

            if (canStore)
                Storage.AddValue(valueT);

            ReceivedNewValue?.Invoke(valueT);

            return canStore;
        }

        internal override bool TryAddValue(byte[] bytes) => TryAddValue(bytes.ToValue<T>());

        internal override List<BaseValue> ConvertValues(List<byte[]> bytesPages) =>
            bytesPages.Select(v => v.ToValue<T>()).ToList();


        internal override void AddPolicy<U>(U policy)
        {
            if (policy is DataPolicy<T> dataPolicy)
                _dataPolicies.Add(dataPolicy);
            else
                base.AddPolicy(policy);
        }

        protected override List<Guid> GetPolicyIds()
        {
            var dataPolicyIds = _dataPolicies.Where(u => u != _typePolicy).Select(u => u.Id);

            return base.GetPolicyIds().AddRangeFluent(dataPolicyIds);
        }


        private bool TryValidate(BaseValue value, out T valueT)
        {
            _dataResult = PolicyResult.Ok;

            valueT = value as T;

            foreach (var policy in _dataPolicies)
            {
                _dataResult += policy.Validate(valueT);

                if (policy == _typePolicy && !_dataResult.IsOk)
                    return false;
            }

            _dataResult += PolicyResult.FromValue(valueT); //add user status

            return true;
        }
    }
}
