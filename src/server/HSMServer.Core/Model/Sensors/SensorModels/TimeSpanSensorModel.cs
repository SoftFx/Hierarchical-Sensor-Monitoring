using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;


namespace HSMServer.Core.Model;

public class TimeSpanSensorModel : BaseSensorModel<TimeSpanValue>
{
    protected override TimeSpanValueStorage Storage { get; } = new TimeSpanValueStorage();


    public override SensorPolicyCollection<TimeSpanValue, TimeSpanPolicy> Policies { get; } = new();

    public override SensorType Type { get; } = SensorType.TimeSpan;


    public TimeSpanSensorModel(SensorEntity entity, IDatabaseCore database) : base(entity, database)
    {
    }
}