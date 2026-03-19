using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;

namespace HSMServer.Core.Model
{
    public sealed class DoubleBarSensorModel : BaseSensorModel<DoubleBarValue>, IBarSensor
    {
        protected override DoubleBarValuesStorage Storage { get; } = new DoubleBarValuesStorage();


        public override SensorPolicyCollection<DoubleBarValue, DoubleBarPolicy> Policies { get; }

        public override SensorType Type { get; } = SensorType.DoubleBar;


        BarBaseValue IBarSensor.LocalLastValue => Storage.PartialLastValue;


        public DoubleBarSensorModel(SensorEntity entity, IDatabaseCore database, IAlertScheduleProvider provider) : base(entity, database) 
        {
            Policies = new(provider);
            Policies.Attach(this);
        }
    }
}
