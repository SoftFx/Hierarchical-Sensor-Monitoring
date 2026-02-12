using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;
using System;


namespace HSMServer.Core.Model;

public class VersionSensorModel : BaseSensorModel<VersionValue>
{
    internal override VersionValueStorage Storage { get; }


    public override SensorPolicyCollection<VersionValue, VersionPolicy> Policies { get; } = new();

    public override SensorType Type { get; } = SensorType.Version;


    public VersionSensorModel(SensorEntity entity, IDatabaseCore database) : base(entity, database)
    {
        Storage = new VersionValueStorage(_getFirstValue, _getLastValue);
    }
}