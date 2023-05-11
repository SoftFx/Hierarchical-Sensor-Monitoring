using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Extensions;
using HSMServer.Core.Model.Policies;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public abstract class BaseSensorModel<T> : BaseSensorModel where T : BaseValue
    {
        private readonly DataPolicyCollection<T> _dataPolicies = new();


        protected override ValuesStorage<T> Storage { get; }

        public override DataPolicyCollection DataPolicies => _dataPolicies;


        protected BaseSensorModel(SensorEntity entity) : base(entity) { }


        internal override bool TryAddValue(BaseValue value)
        {
            var canStore = _dataPolicies.TryValidate(value, out var valueT);

            if (canStore)
                Storage.AddValue(valueT);

            return canStore;
        }

        internal override bool TryAddValue(byte[] bytes) => TryAddValue(bytes.ToValue<T>());

        internal override List<BaseValue> ConvertValues(List<byte[]> bytesPages) =>
            bytesPages.Select(v => v.ToValue<T>()).ToList();

        internal override void Update(SensorUpdate update)
        {
            _dataPolicies.Update(update.DataPolicies);

            base.Update(update);
        }

        internal override void AddPolicy<U>(U policy)
        {
            if (policy is DataPolicy<T> dataPolicy)
                _dataPolicies.Add(dataPolicy);
            else
                base.AddPolicy(policy);
        }
    }
}
