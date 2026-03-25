using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;


namespace HSMServer.Core.Model;

public class TimeSpanSensorModel : BaseSensorModel<TimeSpanValue>
{
    protected override TimeSpanValueStorage Storage { get; } = new TimeSpanValueStorage();


    public override SensorPolicyCollection<TimeSpanValue, TimeSpanPolicy> Policies { get; }

    public override SensorType Type { get; } = SensorType.TimeSpan;


    public TimeSpanSensorModel(SensorEntity entity, IDatabaseCore database, IAlertScheduleProvider provider) : base(entity, database)
    {
        Policies = new(provider);
        Policies.Attach(this);
    }
}