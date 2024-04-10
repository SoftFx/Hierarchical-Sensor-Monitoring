using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Core.Model.NodeSettings
{
    public sealed class TimeIntervalSettingProperty : SettingPropertyBase<TimeIntervalModel>
    {
        protected override TimeIntervalModel EmptyValue => TimeIntervalModel.None;


        public override bool IsSet => CurValue?.IsFromParent ?? false;


        internal TimeIntervalEntity ToEntity() => CurValue?.ToEntity();
    }


    public sealed class DestinationSettingProperty : SettingPropertyBase<PolicyDestination>
    {
        protected override PolicyDestination EmptyValue { get; } = new PolicyDestination();


        public override bool IsSet => CurValue?.UseDefaultChats ?? false;


        internal PolicyDestinationEntity ToEntity() => CurValue?.ToEntity();
    }
}