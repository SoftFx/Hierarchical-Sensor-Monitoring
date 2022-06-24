using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model
{
    public sealed class IntegerSensorModel : BaseSensorModel<IntegerValue>
    {
        public override IntegerValuesStorage Storage { get; } = new();


        internal IntegerSensorModel(SensorEntity entity) : base(entity) { }
    }
}
