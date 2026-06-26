using System;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model.NodeSettings
{
    public class TimeIntervalSettingProperty : SettingPropertyBase<TimeIntervalModel>
    {
        private Func<bool> _isSetOverride;

        protected override TimeIntervalModel EmptyValue => TimeIntervalModel.None;


        public override bool IsSet => _isSetOverride != null ? _isSetOverride() : (!CurValue?.IsFromParent ?? false);

        public override TimeIntervalModel Value =>
            CurValue?.IsFromParent ?? true
                ? _parent?.Value ?? EmptyValue
                : CurValue;

        internal void SetIsSetOverride(Func<bool> isSetOverride) => _isSetOverride = isSetOverride;


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