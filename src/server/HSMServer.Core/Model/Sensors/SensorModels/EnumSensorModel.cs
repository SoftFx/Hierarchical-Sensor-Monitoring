using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;


namespace HSMServer.Core.Model
{
    public sealed class EnumSensorModel : BaseSensorModel<EnumValue>
    {
        internal override EnumValuesStorage Storage { get; } = new EnumValuesStorage();


        public override SensorPolicyCollection<EnumValue, EnumPolicy> Policies { get; } = new();

        public override SensorType Type { get; } = SensorType.Enum;


        public EnumSensorModel(SensorEntity entity) : base(entity) { }

    }
}
