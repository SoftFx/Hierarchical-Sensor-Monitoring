using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model.NodeSettings
{
    public sealed class TimeIntervalSettingProperty : SettingPropertyBase<TimeIntervalModel>
    {
        protected override TimeIntervalModel EmptyValue => TimeIntervalModel.None;


        public override bool IsSet => !CurValue?.IsFromParent ?? false;


        internal TimeIntervalEntity ToEntity() => CurValue?.ToEntity();

        public override string GetJournalValue(string customNone = null)
        {
            string GetEmpty() => customNone ?? EmptyValue.ToString();

            return CurValue is null ? GetEmpty() : CurValue.IsNone ? GetEmpty() : CurValue.ToString();
        }

        public override string ToString() => GetJournalValue();
    }


    public sealed class DestinationSettingProperty : SettingPropertyBase<PolicyDestinationSettings>
    {
        protected override PolicyDestinationSettings EmptyValue { get; } = new();


        public override bool IsSet => !CurValue?.IsFromParent ?? false;


        internal PolicyDestinationSettingsEntity ToEntity() => CurValue?.ToEntity();

        public override string GetJournalValue(string customNone = null)
        {
            return CurValue is null ? customNone ?? EmptyValue.ToString() : CurValue.ToString();
        }
    }
}