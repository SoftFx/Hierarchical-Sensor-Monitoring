using HSMDatabase.AccessManager.DatabaseEntities;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.NodeSettings
{
    public sealed class SettingsCollection
    {
        private readonly Dictionary<string, SettingProperty> _properties = new();


        public SettingProperty<TimeIntervalModel> KeepHistory { get; }

        public SettingProperty<TimeIntervalModel> SelfDestroy { get; }

        public SettingProperty<TimeIntervalModel> TTL { get; }


        internal SettingsCollection()
        {
            KeepHistory = Register<TimeIntervalModel>(nameof(KeepHistory));
            SelfDestroy = Register<TimeIntervalModel>(nameof(SelfDestroy));
            TTL = Register<TimeIntervalModel>(nameof(TTL));
        }


        internal void SetSettings(Dictionary<string, TimeIntervalEntity> entity)
        {
            foreach (var (name, setting) in entity)
                if (_properties.TryGetValue(name, out var property))
                    property.SetValue(new TimeIntervalModel(setting));
        }

        internal void SetParentSettings(SettingsCollection parentCollection)
        {
            foreach (var (name, property) in _properties)
                property.ParentProperty = parentCollection._properties[name];
        }

        internal Dictionary<string, TimeIntervalEntity> ToEntity() =>
            _properties.Where(p => p.Value.IsSet).ToDictionary(k => k.Key, v => v.Value.ToEntity());


        private SettingProperty<T> Register<T>(string name) where T : TimeIntervalModel
        {
            var property = new SettingProperty<T>();

            _properties[name] = property;

            return property;
        }
    }
}