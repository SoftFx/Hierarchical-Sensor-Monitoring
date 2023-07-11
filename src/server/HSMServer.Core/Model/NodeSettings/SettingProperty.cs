using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
using System;
using HSMServer.Core.Journal;

namespace HSMServer.Core.Model.NodeSettings
{
    public abstract class SettingProperty
    {
        internal SettingProperty ParentProperty { get; set; }


        public abstract bool IsEmpty { get; }

        public abstract bool IsSet { get; }


        public Action<ActionType, TimeIntervalModel> Uploaded;


        internal abstract bool TrySetValue(TimeIntervalModel policy, Guid id);

        internal abstract TimeIntervalEntity ToEntity();
    }


    public sealed class SettingProperty<T> : SettingProperty, IJournal where T : TimeIntervalModel
    {
        private readonly T _emptyValue = (T)TimeIntervalModel.Never;
        private T _curValue;


        public event Action<JournalRecordModel> CreateJournal;


        public override bool IsEmpty => Value is null;

        public override bool IsSet => _curValue is not null;

        
        public required string Name { get; set; }

        public T Value => _curValue ?? ((SettingProperty<T>)ParentProperty)?.Value ?? _emptyValue;


        internal override bool TrySetValue(TimeIntervalModel update, Guid id)
        {
            if (update is null)
                return false;

            var action = ActionType.Add;
            var newValue = (T)update;

            var copyValue = _curValue;
            
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

            if (id != Guid.Empty)
            {
                var val1 = GetValue(copyValue);
                var val2 = GetValue(_curValue);
                if (val1 != val2)
                    CreateJournal?.Invoke(new JournalRecordModel(id, DateTime.UtcNow, $"{Name}: {val1} -> {val2}"));
            }
            
            Uploaded?.Invoke(action, newValue);
            return true;
        }

        private static string GetValue(T value)
        {
            if (value is null)
                return TimeInterval.FromParent.ToString();

            if (value.IsFromParent) 
                return TimeInterval.FromParent.ToString();

            if (value.UseCustom)
                return value.Ticks.ToString();

            return value.Interval.ToString();
        }

        internal override TimeIntervalEntity ToEntity() => _curValue?.ToEntity();
    }
}