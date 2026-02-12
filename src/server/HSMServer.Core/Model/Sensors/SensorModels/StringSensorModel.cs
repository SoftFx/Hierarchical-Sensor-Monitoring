using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;


namespace HSMServer.Core.Model
{
    public sealed class StringSensorModel : BaseSensorModel<StringValue>
    {
        internal override StringValuesStorage Storage { get; }


        public override SensorPolicyCollection<StringValue, StringPolicy> Policies { get; } = new();

        public override SensorType Type { get; } = SensorType.String;


        public StringSensorModel(SensorEntity entity, IDatabaseCore database) : base(entity, database)
        {
            Storage = new StringValuesStorage(_getFirstValue, _getLastValue);
        }
    }
}
