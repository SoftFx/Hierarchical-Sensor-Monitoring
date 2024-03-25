using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class TimeToLiveConditionViewModel : ConditionViewModel
    {
        protected override List<AlertProperty> Properties { get; } = [AlertProperty.TimeToLive];


        public TimeToLiveConditionViewModel(bool isMain = true) : base(isMain) { }
    }
}
