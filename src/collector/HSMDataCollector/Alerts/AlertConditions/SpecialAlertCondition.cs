using System;

namespace HSMDataCollector.Alerts
{
    public sealed class SpecialAlertCondition : AlertConditionBase<SpecialAlertTemplate>
    {
        public TimeSpan? TtlValue { get; private set; }


        internal SpecialAlertCondition() : base() { }


        internal SpecialAlertCondition AddTtlValue(TimeSpan? ttlValue)
        {
            TtlValue = ttlValue;
            return this;
        }


        protected override AlertAction<SpecialAlertTemplate> BuildAlertAction() =>
            new SpecialAlertAction(_confirmationPeriod)
            {
                TtlValue = TtlValue,
            };
    }
}