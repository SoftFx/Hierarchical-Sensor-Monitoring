using HSMServer.Core.Model.Policies;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class TimeToLiveConditionViewModel : ConditionViewModel
    {
        protected override List<PolicyProperty> Properties { get; } = new() { PolicyProperty.TimeToLive };


        public TimeToLiveConditionViewModel(bool isMain = true) : base(isMain) { }
    }
}
