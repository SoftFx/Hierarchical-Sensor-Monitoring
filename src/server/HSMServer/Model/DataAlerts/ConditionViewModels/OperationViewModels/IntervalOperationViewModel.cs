namespace HSMServer.Model.DataAlerts
{
    public abstract class IntervalOperationViewModel
    {
        public TimeIntervalViewModel Target { get; } = new();

        public abstract string TargetName { get; }

        public abstract string Operation { get; }


        protected IntervalOperationViewModel(TimeIntervalViewModel interval)
        {
            Target = interval;
        }
    }


    public sealed class TimeToLiveOperation : IntervalOperationViewModel
    {
        public override string TargetName { get; } = nameof(AlertConditionBase.TimeToLive);

        public override string Operation { get; } = "is";


        internal TimeToLiveOperation(TimeIntervalViewModel interval) : base(interval) { }
    }


    public sealed class SensitivityOperation : IntervalOperationViewModel
    {
        public override string TargetName { get; } = nameof(AlertConditionBase.Sensitivity);

        public override string Operation { get; } = "is more than";


        internal SensitivityOperation(TimeIntervalViewModel interval) : base(interval) { }
    }
}
