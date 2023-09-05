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
        private readonly Dictionary<string, SettingProperty> _properties = new();


        public SettingProperty<TimeIntervalModel> KeepHistory { get; }

        public SettingProperty<TimeIntervalModel> SelfDestroy { get; }

        public SettingProperty<TimeIntervalModel> TTL { get; }


        public event Action<JournalRecordModel> ChangesHandler;


        internal SettingsCollection()
        {
            KeepHistory = Register<TimeIntervalModel>(nameof(KeepHistory));
            SelfDestroy = Register<TimeIntervalModel>(nameof(SelfDestroy));
            TTL = Register<TimeIntervalModel>(nameof(TTL));
        }


        internal void Update(BaseNodeUpdate update, ChangeInfoTable table)
        {
            void Update(SettingProperty<TimeIntervalModel> setting, TimeIntervalModel newVal, [CallerArgumentExpression(nameof(setting))] string propName = "", NoneValues none = NoneValues.Never)
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
        }


        internal void SetSettings(Dictionary<string, TimeIntervalEntity> entity)
        {
            foreach (var (name, setting) in entity)
                if (_properties.TryGetValue(name, out var property))
                    property.TrySetValue(new TimeIntervalModel(setting));
        }

        internal void SetParentSettings(SettingsCollection parentCollection)
        {
            foreach (var (name, property) in _properties)
                property.ParentProperty = parentCollection._properties[name];
        }

        internal Dictionary<string, TimeIntervalEntity> ToEntity() =>
            _properties.Where(p => p.Value.IsSet).ToDictionary(k => k.Key, v => v.Value.ToEntity());


        private SettingProperty<T> Register<T>(string name) where T : TimeIntervalModel, new()
        {
            var property = new SettingProperty<T>()
            {
                Name = name
            };

            _properties[name] = property;

            return property;
        }
    }
}