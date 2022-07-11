using HSMServer.Core.DataLayer;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public abstract class BaseSensorModel<T> : BaseSensorModel where T : BaseValue
    {
        private readonly List<Policy<T>> _userPolicies = new();


        protected override ValuesStorage<T> Storage { get; }


        // TODO : false only if there is some system exception or smth like this (because if false - value is not saved to db)
        internal override bool TryAddValue(BaseValue value, out BaseValue cachedValue)
        {
            if (value is T valueT)
            {
                cachedValue = Storage.AddValue(valueT);
                return true;
            }

            cachedValue = default;
            return false;
        }

        internal override void AddValue(byte[] valueBytes)
        {
            var value = valueBytes.ConvertToSensorValue<T>();

            if (value != null && value is T valueT)
                Storage.AddValue(valueT);
        }


        internal override void AddPolicy(Policy policy)
        {
            if (policy is Policy<T> customPolicy)
                _userPolicies.Add(customPolicy);
            else
                base.AddPolicy(policy);
        }

        protected override List<string> GetPolicyIds()
        {
            var policyIds = new List<string>(_systemPolicies.Count + _userPolicies.Count);

            policyIds.AddRange(_systemPolicies.Select(p => p.Id.ToString()));
            policyIds.AddRange(_userPolicies.Select(p => p.Id.ToString()));

            return policyIds;
        }
    }
}
