using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.TableOfChanges
{
    internal sealed class ChangeInfoTable
    {
        private readonly Func<string> _getFullPath;


        public GuidChangeCollection Policies { get; private set; } = new();

        public ChangeCollection Properties { get; private set; } = new();

        public ChangeCollection Settings { get; private set; } = new();


        public ChangeInfo TtlPolicy { get; private set; }


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

            Policies = new(entity.Policies.ToDictionary(k => new Guid(k.Key), v => new ChangeInfo(v.Value)));
            Properties = BuildCollection(entity.Properties);
            Settings = BuildCollection(entity.Settings);

            TtlPolicy = new ChangeInfo(entity.TTLPolicy);
        }

        public ChangeInfoTableEntity ToEntity() =>
            new()
            {
                Policies = Policies.ToEntity(),
                Settings = Settings.ToEntity(),
                Properties = Properties.ToEntity(),
            };
    }
}