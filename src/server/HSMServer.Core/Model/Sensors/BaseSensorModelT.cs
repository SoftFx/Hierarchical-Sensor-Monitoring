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
            var isLastValue = Storage.LastValue is null || value.Time >= Storage.LastValue.Time;
            var canStore = Policies.TryValidate(value, out var valueT, isLastValue);

            if (canStore)
            {
                Storage.AddValue(valueT);

                ReceivedNewValue?.Invoke(valueT);
            }

            return canStore;
        }

        internal override List<BaseValue> ConvertValues(List<byte[]> pages, bool includeTTL = true) => includeTTL ? pages.Select(Convert).ToList() : pages.Select(Convert).Where(x => x.Comment != "#Timeout").ToList();

        internal override void AddDbValue(byte[] bytes) => Storage.AddValue((T)Convert(bytes));

        internal override bool CheckTimeout(bool toNotify = true) => Policies.SensorTimeout(LastValue?.ReceivingTime, toNotify);

        internal override void RecalculatePolicy()
        {
            if (LastValue is not null)
                Policies.TryValidate(LastValue, out _);
        }


        private BaseValue Convert(byte[] bytes) => bytes.ToValue<T>();
    }
}