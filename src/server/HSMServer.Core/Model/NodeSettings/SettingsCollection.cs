using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Journal;
using HSMServer.Core.Model.Policies;
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
            void Update(SettingPropertyBase<TimeIntervalModel> setting, TimeIntervalModel newVal, [CallerArgumentExpression(nameof(setting))] string propName = "", NoneValues none = NoneValues.Never)
            {
                var nodeInfo = table.Settings[propName];
                var oldVal = setting.CurValue;

                if (nodeInfo.CanChange(update.Initiator) && setting.TrySetValue(newVal))
                {
                    ChangesHandler?.Invoke(new JournalRecordModel(update.Id, update.Initiator)
                    {
                        Enviroment = "Settings update",
                        OldValue = oldVal.IsNone ? $"{none}" : $"{oldVal}",
                        NewValue = newVal.IsNone ? $"{none}" : $"{newVal}",

                        PropertyName = propName,
                        Path = table.Path,
                    });

                    nodeInfo.SetUpdate(update.Initiator);
                }
            }

            Update(TTL, update.TTL);
            Update(SelfDestroy, update.SelfDestroy, "Remove sensor after inactivity");
            Update(KeepHistory, update.KeepHistory, "Keep sensor history", NoneValues.Forever);

            //add default chats update
        }


        internal void SetSettings(Dictionary<string, TimeIntervalEntity> settingsEntity, PolicyDestinationEntity defaultChats)
        {
            foreach (var (name, setting) in settingsEntity)
                if (_intervalProperties.TryGetValue(name, out var property))
                    property.TrySetValue(new TimeIntervalModel(setting));

            DefaultChats.TrySetValue(new PolicyDestination(defaultChats));
        }

        internal void SetParentSettings(SettingsCollection parentCollection)
        {
            foreach (var (name, property) in _intervalProperties)
                property.ParentProperty = parentCollection._intervalProperties[name];

            DefaultChats.ParentProperty = parentCollection.DefaultChats;
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