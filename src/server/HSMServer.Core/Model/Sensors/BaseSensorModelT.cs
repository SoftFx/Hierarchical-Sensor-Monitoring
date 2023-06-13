using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Extensions;
using HSMServer.Core.Model.Policies;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public abstract class BaseSensorModel<T> : BaseSensorModel where T : BaseValue
    {
        internal override ValuesStorage<T> Storage { get; }

        public override DataPolicyCollection<T> DataPolicies { get; }


        protected BaseSensorModel(SensorEntity entity) : base(entity) { }


        internal override bool TryAddValue(BaseValue value)
        {
            var canStore = DataPolicies.TryValidate(value, out var valueT);

            if (canStore)
            {
                Storage.AddValue(valueT);

                ReceivedNewValue?.Invoke(valueT);
            }

            return canStore;
        }

        internal override bool TryAddValue(byte[] bytes) => TryAddValue(bytes.ToValue<T>());

        internal override List<BaseValue> ConvertValues(List<byte[]> bytesPages) =>
            bytesPages.Select(v => v.ToValue<T>()).ToList();

        internal override void AddPolicy<U>(U policy)
        {
            if (policy is DataPolicy<T> dataPolicy)
                DataPolicies.Add(dataPolicy);
            else
                base.AddPolicy(policy);
        }
    }
}