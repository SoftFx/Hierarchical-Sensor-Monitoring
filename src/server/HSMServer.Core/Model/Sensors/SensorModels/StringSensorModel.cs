using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model
{
    public sealed class StringSensorModel : BaseSensorModel<StringValue>
    {
        protected override StringValuesStorage Storage { get; } = new StringValuesStorage();

        public override SensorType Type { get; } = SensorType.String;


        public StringSensorModel(SensorEntity entity) : base(entity) { }
    }
}
