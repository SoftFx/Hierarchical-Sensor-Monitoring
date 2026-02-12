using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Core.Model
{
    public sealed class IntegerSensorModel : BaseSensorModel<IntegerValue>
    {
        internal override IntegerValuesStorage Storage { get; }


        public override SensorPolicyCollection<IntegerValue, IntegerPolicy> Policies { get; } = new();

        public override SensorType Type { get; } = SensorType.Integer;


        public IntegerSensorModel(SensorEntity entity, IDatabaseCore database) : base(entity, database)
        {
            Storage = new IntegerValuesStorage(_getFirstValue, _getLastValue);
        }
    }
}
