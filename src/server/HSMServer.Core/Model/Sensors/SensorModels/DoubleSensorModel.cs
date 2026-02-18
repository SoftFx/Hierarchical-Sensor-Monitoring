using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;


namespace HSMServer.Core.Model
{
    public sealed class DoubleSensorModel : BaseSensorModel<DoubleValue>
    {
        protected override DoubleValuesStorage Storage { get; } = new DoubleValuesStorage();


        public override SensorPolicyCollection<DoubleValue, DoublePolicy> Policies { get; } = new();

        public override SensorType Type { get; } = SensorType.Double;


        public DoubleSensorModel(SensorEntity entity, IDatabaseCore database) : base(entity, database)
        {
        }
    }
}
