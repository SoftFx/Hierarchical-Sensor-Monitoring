using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model.NodeSettings
{
    public sealed class TimeIntervalSettingProperty : SettingPropertyBase<TimeIntervalModel>
    {
        protected override TimeIntervalModel EmptyValue => TimeIntervalModel.None;


        public override bool IsSet => !CurValue?.IsFromParent ?? false;


        internal TimeIntervalEntity ToEntity() => CurValue?.ToEntity();
    }


    public sealed class DestinationSettingProperty : SettingPropertyBase<PolicyDestinationSettings>
    {
        protected override PolicyDestinationSettings EmptyValue { get; } = new();


        public override bool IsSet => !CurValue?.IsFromParent ?? false;


        internal PolicyDestinationSettingsEntity ToEntity() => CurValue?.ToEntity();
    }
}