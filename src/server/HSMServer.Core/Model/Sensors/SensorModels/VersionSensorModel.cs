using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model;

public class VersionSensorModel : BaseSensorModel<VersionValue>
{
    protected override VersionValueStorage Storage { get; } = new VersionValueStorage();
    
    public override SensorType Type { get; } = SensorType.Version;
    
    
    public VersionSensorModel(SensorEntity entity) : base(entity) { }
}