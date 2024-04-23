using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Journal;
using HSMServer.Core.TableOfChanges;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace HSMServer.Core.Model.NodeSettings
{
    public sealed class SettingsCollection : IChangesEntity
    {
        private readonly Dictionary<string, TimeIntervalSettingProperty> _intervalProperties = [];


        public DestinationSettingProperty DefaultChats { get; } = new();


        public TimeIntervalSettingProperty KeepHistory { get; }

        public TimeIntervalSettingProperty SelfDestroy { get; }

        public TimeIntervalSettingProperty TTL { get; }


        public event Action<JournalRecordModel> ChangesHandler;


        internal SettingsCollection()
        {
            KeepHistory = Register(nameof(KeepHistory));
            SelfDestroy = Register(nameof(SelfDestroy));
            TTL = Register(nameof(TTL));
        }


        internal void Update(BaseNodeUpdate update, ChangeInfoTable table)
        {
            void Update<T>(SettingPropertyBase<T> setting, T newVal, [CallerArgumentExpression(nameof(setting))] string propName = "", object emptyValue = null)
                where T : class, new()
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

            Update(TTL, update.TTL, emptyValue: NoneValues.Never);
            Update(SelfDestroy, update.SelfDestroy, "Remove sensor after inactivity", NoneValues.Never);
            Update(KeepHistory, update.KeepHistory, "Keep sensor history", NoneValues.Forever);
            Update(DefaultChats, update.DefaultChats, "Default telegram chats");
        }


        internal void SetSettings(Dictionary<string, TimeIntervalEntity> settingsEntity, PolicyDestinationSettingsEntity defaultChats)
        {
            foreach (var (name, setting) in settingsEntity)
                if (_intervalProperties.TryGetValue(name, out var property))
                    property.TrySetValue(new TimeIntervalModel(setting));

            DefaultChats.TrySetValue(new PolicyDestinationSettings(defaultChats));
        }

        internal void SetParentSettings(SettingsCollection parentCollection)
        {
            foreach (var (name, property) in _intervalProperties)
                property.SetParent(parentCollection._intervalProperties[name]);

            DefaultChats.SetParent(parentCollection.DefaultChats);
        }

        internal Dictionary<string, TimeIntervalEntity> ToEntity() =>
            _intervalProperties.Where(p => p.Value.IsSet).ToDictionary(k => k.Key, v => v.Value.ToEntity());


        private TimeIntervalSettingProperty Register(string name)
        {
            _intervalProperties[name] = new TimeIntervalSettingProperty();

            return _intervalProperties[name];
        }
    }
}