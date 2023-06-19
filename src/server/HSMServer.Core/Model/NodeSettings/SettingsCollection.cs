using HSMDatabase.AccessManager.DatabaseEntities;
using System;
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
            KeepHistory = Register<TimeIntervalModel>();
            SelfDestroy = Register<TimeIntervalModel>();
            TTL = Register<TimeIntervalModel>();
        }


        internal void SetSettings(Dictionary<string, TimeIntervalEntity> entity)
        {
            foreach (var (name, setting) in entity)
                if (_properties.TryGetValue(name, out var property))
                    property.SetValue(new TimeIntervalModel(setting));
        }

        internal void SetParentSettings(SettingsCollection parentCollection)
        {
            foreach (var (policyType, property) in _properties)
                property.ParentProperty = parentCollection._properties[policyType];
        }

        internal bool HasUpdateTimeout(DateTime? receivingTime)
        {
            //var oldResult = Result;

            //Result -= ExpectedUpdate.Policy.Fail;

            //if (!receivingTime.HasValue)
            //    return false;

            //Result += ExpectedUpdate.Policy.Validate(receivingTime.Value);

            //return Result != oldResult;

            return false;
        }

        internal Dictionary<string, TimeIntervalEntity> ToEntity() =>
            _properties.Where(p => p.Value.IsSet).ToDictionary(k => k.Key, v => v.Value.ToEntity());


        private SettingProperty<T> Register<T>() where T : TimeIntervalModel, new()
        {
            var property = new SettingProperty<T>();

            _properties[typeof(T).Name] = property;

            return property;
        }
    }
}