using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Core.Model;

public class VersionSensorModel : BaseSensorModel<VersionValue>
{
    internal override VersionValueStorage Storage { get; } = new VersionValueStorage();


    public override DataPolicyCollection<VersionValue, VersionDataPolicy> DataPolicies { get; } = new();

    public override SensorType Type { get; } = SensorType.Version;


    public VersionSensorModel(SensorEntity entity) : base(entity) { }
}