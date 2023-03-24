namespace HSMServer.Core.Model;

public class TimeSpanSensorModel : BaseSensorModel<TimeSpanValue>
{
    protected override TimeSpanValueStorage Storage { get; } = new TimeSpanValueStorage();

    public override SensorType Type { get; } = SensorType.TimeSpan;
}