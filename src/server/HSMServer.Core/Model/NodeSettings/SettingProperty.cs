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


        internal abstract bool TrySetValue(TimeIntervalModel policy, Guid id = default, string path = "", string initiator = "");

        internal abstract TimeIntervalEntity ToEntity();
    }


    public sealed class SettingProperty<T> : SettingProperty, IChangesEntity where T : TimeIntervalModel, new()
    {
        private readonly T _emptyValue = (T)TimeIntervalModel.None;


        public event Action<JournalRecordModel> ChangesHandler;


        public override bool IsEmpty => Value is null;

        public override bool IsSet => !CurValue?.IsFromParent ?? false;

        
        public required string Name { get; set; }

        public T CurValue { get; private set; } = new T();

        public T Value => IsSet ? CurValue : ((SettingProperty<T>)ParentProperty)?.Value ?? _emptyValue;


        internal override bool TrySetValue(TimeIntervalModel update, Guid id = default, string path = "", string initiator = "")
        {
            var newValue = (T)update;

            if (newValue is not null && CurValue != newValue)
            {
                if (id != default)
                {
                    var cur = GetValue(CurValue);
                    var updated = GetValue(newValue);
                    if (cur != updated )
                        ChangesHandler?.Invoke(new JournalRecordModel(id, DateTime.UtcNow, $"{Name}: {cur} -> {updated}", path, RecordType.Changes, initiator));
                }
               
                CurValue = newValue;

                Uploaded?.Invoke(ActionType.Update, newValue);
            }

            return newValue is null;
        }
        
        private static string GetValue(T value)
        {
            if (value is null)
                return TimeInterval.FromParent.ToString();

            if (value.IsFromParent) 
                return TimeInterval.FromParent.ToString();

            if (value.UseTicks)
                return new TimeSpan(value.Ticks).ToString();

            return value.Interval.ToString();
        }

        internal override TimeIntervalEntity ToEntity() => CurValue?.ToEntity();
    }
}