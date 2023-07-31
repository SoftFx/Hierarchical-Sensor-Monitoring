using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
using System;

namespace HSMServer.Core.Model.NodeSettings
{
    public abstract class SettingProperty
    {
        internal SettingProperty ParentProperty { get; set; }


        public abstract bool IsEmpty { get; }

        public abstract bool IsSet { get; }


        public Action<ActionType, TimeIntervalModel> Uploaded;


        internal abstract bool TrySetValue(TimeIntervalModel policy);

        internal abstract TimeIntervalEntity ToEntity();
    }


    public sealed class SettingProperty<T> : SettingProperty where T : TimeIntervalModel, new()
    {
        private readonly T _emptyValue = (T)TimeIntervalModel.None;


        public override bool IsSet => !CurValue?.IsFromParent ?? false;

        public override bool IsEmpty => Value is null;


        public required string Name { get; set; }


        public T CurValue { get; private set; } = new T();

        public T Value => IsSet ? CurValue : ((SettingProperty<T>)ParentProperty)?.Value ?? _emptyValue;


        internal override bool TrySetValue(TimeIntervalModel update)
        {
            var newValue = (T)update;

            if (newValue is not null && CurValue.ToString() != newValue.ToString())
            {
                CurValue = newValue;

                Uploaded?.Invoke(ActionType.Update, newValue);

                return true;
            }

            return false;
        }


        internal override TimeIntervalEntity ToEntity() => CurValue?.ToEntity();
    }
}