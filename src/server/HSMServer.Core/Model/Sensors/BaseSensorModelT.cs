using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Extensions;
using HSMServer.Core.Model.Policies;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public abstract class BaseSensorModel<T> : BaseSensorModel where T : BaseValue
    {
        public override SensorPolicyCollection<T> Policies { get; }

        internal override ValuesStorage<T> Storage { get; }


        protected BaseSensorModel(SensorEntity entity) : base(entity) { }


        internal override bool TryAddValue(BaseValue value)
        {
            var canStore = Policies.TryValidate(value, out var valueT);

            if (canStore)
            {
                Storage.AddValue(valueT);

                ReceivedNewValue?.Invoke(valueT);
            }

            return canStore;
        }


        internal override bool CheckTimeout() => Policies.SensorTimeout(LastValue?.ReceivingTime);

        internal override void AddDbValue(byte[] bytes) => Storage.AddValue((T)Convert(bytes));

        internal override List<BaseValue> ConvertValues(List<byte[]> pages) => pages.Select(Convert).ToList();


        private BaseValue Convert(byte[] bytes) => bytes.ToValue<T>();
    }
}