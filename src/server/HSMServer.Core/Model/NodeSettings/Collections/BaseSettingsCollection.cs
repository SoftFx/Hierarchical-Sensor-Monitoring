using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Journal;
using HSMServer.Core.TableOfChanges;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.NodeSettings
{
    public abstract class BaseSettingsCollection : IChangesEntity
    {
        private readonly Dictionary<string, TimeIntervalSettingProperty> _intervalProperties = [];

        public TimeIntervalSettingProperty KeepHistory { get; }

        public TimeIntervalSettingProperty SelfDestroy { get; }

        public TimeIntervalSettingProperty TTL { get; }


        public event Action<JournalRecordModel> ChangesHandler;


        internal BaseSettingsCollection()
        {
            KeepHistory = Register(nameof(KeepHistory));
            SelfDestroy = Register(nameof(SelfDestroy));
            TTL = Register(nameof(TTL));
        }


        internal virtual void Update(BaseNodeUpdate update, ChangeInfoTable table)
        {
            var updateAndRecord = GetUpdateFunction<TimeIntervalModel>(update, table);

            updateAndRecord(TTL, update.TTL, "TTL", NoneValues.Never);
            updateAndRecord(SelfDestroy, update.SelfDestroy, "Remove sensor after inactivity", NoneValues.Never);
            updateAndRecord(KeepHistory, update.KeepHistory, "Keep sensor history", NoneValues.Forever);
        }

        internal void SetSettings(Dictionary<string, TimeIntervalEntity> settingsEntity)
        {
            foreach (var (name, setting) in settingsEntity)
                if (_intervalProperties.TryGetValue(name, out var property))
                    property.TrySetValue(new TimeIntervalModel(setting));
        }

        internal virtual void SetParentSettings(BaseSettingsCollection parentCollection)
        {
            foreach (var (name, property) in _intervalProperties)
                property.SetParent(parentCollection._intervalProperties[name]);
        }

        internal Dictionary<string, TimeIntervalEntity> ToEntity() =>
            _intervalProperties.Where(p => p.Value.IsSet).ToDictionary(k => k.Key, v => v.Value.ToEntity());


        private protected Action<SettingPropertyBase<T>, T, string, object> GetUpdateFunction<T>(BaseNodeUpdate update, ChangeInfoTable table)
            where T : class, new()
        {
            void UpdateAndRecord(SettingPropertyBase<T> setting, T newVal, string propName = "", object emptyValue = null)
            {
                var nodeInfo = table.Settings[propName];
                var defaultValue = emptyValue?.ToString();
                var oldVal = setting.GetJournalValue(defaultValue);

                if (nodeInfo.CanChange(update.Initiator) && setting.TrySetValue(newVal))
                {
                    ChangesHandler?.Invoke(new JournalRecordModel(update.Id, update.Initiator)
                    {
                        Enviroment = "Settings update",
                        OldValue = oldVal,
                        NewValue = setting.GetJournalValue(defaultValue),

                        PropertyName = propName,
                        Path = table.Path,
                    });

                    nodeInfo.SetUpdate(update.Initiator);
                }
            }

            return UpdateAndRecord;
        }

        private TimeIntervalSettingProperty Register(string name)
        {
            _intervalProperties[name] = new TimeIntervalSettingProperty();

            return _intervalProperties[name];
        }
    }
}