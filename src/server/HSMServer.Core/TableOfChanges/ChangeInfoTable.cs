using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.TableOfChanges
{
    internal sealed class ChangeInfoTable
    {
        private readonly Func<string> _getFullPath;


        public ChangeCollection Properties { get; private set; } = new();

        public ChangeCollection Settings { get; private set; } = new();

        public ChangeCollection Policies { get; private set; } = new();


        public ChangeCollection TtlPolicies { get; private set; } = new();


        public string Path => _getFullPath?.Invoke();


        public ChangeInfoTable(Func<string> getPath)
        {
            _getFullPath = getPath;
        }


        public void FromEntity(ChangeInfoTableEntity entity)
        {
            static ChangeCollection BuildCollection(Dictionary<string, ChangeInfoEntity> collection) =>
                new(collection.ToDictionary(k => k.Key, v => new ChangeInfo(v.Value)));

            entity ??= new ChangeInfoTableEntity();

            Properties = BuildCollection(entity.Properties);
            Policies = BuildCollection(entity.Policies);
            Settings = BuildCollection(entity.Settings);

            // Migration: handle old single TTLPolicy field
            TtlPolicies = entity.TTLPolicies?.Count > 0
                ? BuildCollection(entity.TTLPolicies)
                : entity.TTLPolicy != null
                    ? new(new Dictionary<string, ChangeInfo> { ["0"] = new(entity.TTLPolicy) })
                    : new();
        }

        public ChangeInfoTableEntity ToEntity() =>
            new()
            {
                Policies = Policies.ToEntity(),
                Settings = Settings.ToEntity(),
                Properties = Properties.ToEntity(),

                TTLPolicies = TtlPolicies.ToEntity(),
            };
    }
}
