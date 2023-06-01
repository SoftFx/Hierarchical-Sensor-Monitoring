using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Core.Model;

public class TimeSpanSensorModel : BaseSensorModel<TimeSpanValue>
{
    protected override TimeSpanValueStorage Storage { get; } = new TimeSpanValueStorage();


    public override DataPolicyCollection<TimeSpanValue, TimeSpanDataPolicy> DataPolicies { get; } = new();

    public override SensorType Type { get; } = SensorType.TimeSpan;


    public TimeSpanSensorModel(SensorEntity entity) : base(entity) { }
}