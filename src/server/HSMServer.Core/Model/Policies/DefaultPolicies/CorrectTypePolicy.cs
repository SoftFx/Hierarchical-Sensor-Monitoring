using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model.Policies
{
    internal sealed class CorrectTypePolicy<T> : DefaultPolicyBase where T : BaseValue
    {
        private const SensorStatus FatalStatus = SensorStatus.Error;


        internal CorrectTypePolicy(BaseSensorModel sensor) =>
            Apply(new PolicyEntity
            {
                Id = Id.ToByteArray(),
                SensorStatus = (byte)FatalStatus,
                Icon = FatalStatus.ToIcon(),
                Template = $"Sensor value type is not {typeof(T).Name}",
            }, sensor);


        internal static bool Validate(T value) => value is not null;
    }
}