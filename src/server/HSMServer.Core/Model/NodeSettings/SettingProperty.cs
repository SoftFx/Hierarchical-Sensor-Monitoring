using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
using System;

namespace HSMServer.Core.Model.NodeSettings
{
    public abstract class SettingProperty : IJournalValue
    {
        internal SettingProperty ParentProperty { get; set; }


        public abstract bool IsEmpty { get; }

        public abstract bool IsSet { get; }


        public Action<ActionType, TimeIntervalModel> Uploaded;


        internal abstract bool TrySetValue(TimeIntervalModel policy);

        internal abstract TimeIntervalEntity ToEntity();
        
        public virtual string GetValue() => string.Empty;
    }


    public sealed class SettingProperty<T> : SettingProperty where T : TimeIntervalModel
    {
        private readonly T _emptyValue = (T)TimeIntervalModel.Never;
        private T _curValue;


        public override bool IsEmpty => Value is null;

        public override bool IsSet => _curValue is not null;


        public T Value => _curValue ?? ((SettingProperty<T>)ParentProperty)?.Value ?? _emptyValue;


        internal override bool TrySetValue(TimeIntervalModel update)
        {
            if (update is null)
                return false;

            var action = ActionType.Add;
            var newValue = (T)update;

            if (IsSet)
            {
                if (newValue.IsFromParent)
                {
                    _curValue = null;
                    action = ActionType.Delete;
                }
                else
                {
                    _curValue = newValue;
                    action = ActionType.Update;
                }
            }
            else if (!newValue.IsFromParent)
                _curValue = newValue;
            else
                return true;

            Uploaded?.Invoke(action, newValue);

            return true;
        }

        public override string GetValue()
        {
            if (!IsSet)
                return TimeInterval.FromParent.ToString();
            
            if (_curValue.IsFromParent)
                return TimeInterval.FromParent.ToString();

            if (_curValue.UseCustom)
                return _curValue.Ticks.ToString();
            
            return _curValue.Interval.ToString();
        }

        internal override TimeIntervalEntity ToEntity() => _curValue?.ToEntity();
    }
}