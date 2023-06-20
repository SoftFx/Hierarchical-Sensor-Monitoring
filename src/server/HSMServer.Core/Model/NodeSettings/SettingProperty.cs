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


        internal abstract void SetValue(TimeIntervalModel policy);

        internal abstract TimeIntervalEntity ToEntity();
    }


    public sealed class SettingProperty<T> : SettingProperty where T : TimeIntervalModel, new()
    {
        private readonly T _emptyValue = new();
        private T _curValue;


        public override bool IsEmpty => Value is null;

        public override bool IsSet => _curValue is not null;


        public T Value => _curValue ?? ((SettingProperty<T>)ParentProperty)?.Value ?? _emptyValue;


        internal override void SetValue(TimeIntervalModel update)
        {
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
                return;

            Uploaded?.Invoke(action, newValue);
        }

        internal override TimeIntervalEntity ToEntity() => _curValue?.ToEntity();
    }
}