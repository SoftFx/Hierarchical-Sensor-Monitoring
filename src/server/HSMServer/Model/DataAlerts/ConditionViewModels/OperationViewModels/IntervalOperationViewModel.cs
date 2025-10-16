﻿namespace HSMServer.Model.DataAlerts
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
        public override string TargetName { get; } = nameof(ConditionViewModel.TimeToLive);

        public override string Operation { get; } = "is";


        internal TimeToLiveOperation(TimeIntervalViewModel interval) : base(interval) { }
    }


    public sealed class ConfirmationPeriodOperation : IntervalOperationViewModel
    {
        public override string TargetName { get; } = nameof(ConditionViewModel.ConfirmationPeriod);

        public override string Operation { get; } = "is more than";


        internal ConfirmationPeriodOperation(TimeIntervalViewModel interval) : base(interval) { }
    }
}
