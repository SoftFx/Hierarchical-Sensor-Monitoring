using System;

namespace HSMDataCollector.Alerts
{
    public sealed class SpecialAlertAction : AlertAction<SpecialAlertTemplate>
    {
        public TimeSpan? TtlValue { get; internal set; }


        internal SpecialAlertAction() : base(null) { }


        public override SpecialAlertTemplate Build()
        {
            var request =  base.Build();

            request.TtlValue = TtlValue;

            return request;
        }
    }
}