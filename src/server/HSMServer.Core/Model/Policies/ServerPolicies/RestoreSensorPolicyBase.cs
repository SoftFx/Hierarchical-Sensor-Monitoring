namespace HSMServer.Core.Model.Policies
{
    public class RestoreErrorPolicy : RestoreSensorPolicyBase
    {
        protected override SensorStatus TargetStatus => SensorStatus.Error;
    }


    public class RestoreWarningPolicy : RestoreSensorPolicyBase
    {
        protected override SensorStatus TargetStatus => SensorStatus.Warning;
    }


    public class RestoreOffTimePolicy : RestoreSensorPolicyBase
    {
        protected override SensorStatus TargetStatus => SensorStatus.OffTime;
    }


    public abstract class RestoreSensorPolicyBase : ServerPolicy
    {
        protected override SensorStatus FailStatus => SensorStatus.Error;

        protected override string FailMessage => "Sensor not restored";


        protected abstract SensorStatus TargetStatus { get; }


        internal PolicyResult Validate(BaseValue value)
        {
            return value.Status != TargetStatus ? Ok : base.Validate(value.ReceivingTime);
        }
    }
}
