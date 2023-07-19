using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Journal;
using System;

namespace HSMServer.Core.Model.NodeSettings
{
    public abstract class SettingProperty
    {
        internal SettingProperty ParentProperty { get; set; }


        public abstract bool IsSet { get; }


        public Action<ActionType, TimeIntervalModel> Uploaded;


        internal abstract bool TrySetValue(TimeIntervalModel policy);

        internal abstract TimeIntervalEntity ToEntity();
    }


    public sealed class SettingProperty<T> : SettingProperty where T : TimeIntervalModel, new()
    {
        private readonly T _emptyValue = (T)TimeIntervalModel.None;


        public override bool IsSet => !CurValue?.IsFromParent ?? false;


        public required string Name { get; set; }


        public T CurValue { get; private set; } = new T();

        public T Value => IsSet ? CurValue : ((SettingProperty<T>)ParentProperty)?.Value ?? _emptyValue;


        internal override bool TrySetValue(TimeIntervalModel update)
        {
            var newValue = (T)update;

            if (newValue is not null && CurValue != newValue)
            {
                CurValue = newValue;

                Uploaded?.Invoke(ActionType.Update, newValue);
            }

            return newValue is null;
        }

        internal void Update(TimeIntervalModel update, BaseNodeUpdate nodeUpdate, string path, Func<bool> callbackFunction = null)
        {
            var oldValue = CurValue;

            if (!TrySetValue(update) && oldValue != CurValue.ToString())
            {
                ChangesHandler?.Invoke(new JournalRecordModel(nodeUpdate.Id, $"{JournalConstants.CleanUpSettings}{Environment.NewLine}Old {Name}: {oldValue}{Environment.NewLine}New {Name}: {CurValue}", path, nodeUpdate.Initiator));
            }

            callbackFunction?.Invoke();
        }

        internal override TimeIntervalEntity ToEntity() => CurValue?.ToEntity();
    }
}