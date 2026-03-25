using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;

namespace HSMServer.Core.Model
{
    public sealed class IntegerBarSensorModel : BaseSensorModel<IntegerBarValue>, IBarSensor
    {
        protected override IntegerBarValuesStorage Storage { get; } = new IntegerBarValuesStorage();


        public override SensorPolicyCollection<IntegerBarValue, IntegerBarPolicy> Policies { get; }

        public override SensorType Type { get; } = SensorType.IntegerBar;


        BarBaseValue IBarSensor.LocalLastValue => Storage.PartialLastValue;


        public IntegerBarSensorModel(SensorEntity entity, IDatabaseCore database, IAlertScheduleProvider provider) : base(entity, database)
        {
            Policies = new(provider);
            Policies.Attach(this);
        }
    }
}
