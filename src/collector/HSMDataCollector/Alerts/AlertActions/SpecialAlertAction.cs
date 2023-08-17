using System;

namespace HSMDataCollector.Alerts
{
    public sealed class SpecialAlertAction : AlertAction<SpecialAlertBuildRequest>
    {
        public TimeSpan? TtlValue { get; internal set; }


        internal SpecialAlertAction() : base(null) { }


        public override SpecialAlertBuildRequest Build()
        {
            var request =  base.Build();

            request.TtlValue = TtlValue;

            return request;
        }
    }
}