using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Core.Model
{
    public sealed class StringSensorModel : BaseSensorModel<StringValue>
    {
        protected override StringValuesStorage Storage { get; } = new StringValuesStorage();

        public override SensorType Type { get; } = SensorType.String;


        public StringSensorModel(SensorEntity entity) : base(entity) { }


        internal override BaseSensorModel InitDataPolicy()
        {
            AddPolicy(new StringValueLengthPolicy());

            return base.InitDataPolicy();
        }
    }
}
