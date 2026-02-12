using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;


namespace HSMServer.Core.Model
{
    public sealed class BooleanSensorModel : BaseSensorModel<BooleanValue>
    {
        internal override BooleanValuesStorage Storage { get; }


        public override SensorPolicyCollection<BooleanValue, BooleanPolicy> Policies { get; } = new();

        public override SensorType Type { get; } = SensorType.Boolean;


        public BooleanSensorModel(SensorEntity entity, IDatabaseCore database) : base(entity, database)
        {
            Storage = new BooleanValuesStorage(_getFirstValue, _getLastValue);
        }
    }
}
