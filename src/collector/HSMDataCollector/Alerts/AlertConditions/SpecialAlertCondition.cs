using System;

namespace HSMDataCollector.Alerts
{
    public sealed class SpecialAlertCondition : AlertConditionBase<SpecialAlertBuildRequest>
    {
        public TimeSpan? TtlValue { get; private set; }


        internal SpecialAlertCondition() : base() { }


        internal SpecialAlertCondition AddTtlValue(TimeSpan? ttlValue)
        {
            TtlValue = ttlValue;
            return this;
        }


        protected override AlertAction<SpecialAlertBuildRequest> BuildAlertAction() =>
            new SpecialAlertAction()
            {
                TtlValue = TtlValue,
            };
    }
}