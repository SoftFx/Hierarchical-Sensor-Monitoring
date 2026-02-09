using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Core.Model
{
    public sealed class DoubleBarSensorModel : BaseSensorModel<DoubleBarValue>, IBarSensor
    {
        internal override DoubleBarValuesStorage Storage { get; }


        public override SensorPolicyCollection<DoubleBarValue, DoubleBarPolicy> Policies { get; } = new();

        public override SensorType Type { get; } = SensorType.DoubleBar;


        BarBaseValue IBarSensor.LocalLastValue => Storage.PartialLastValue;


        public DoubleBarSensorModel(SensorEntity entity, IDatabaseCore database) : base(entity, database) 
        {
           Storage = new DoubleBarValuesStorage(_getFirstValue, _getLastValue);
        }
    }
}
