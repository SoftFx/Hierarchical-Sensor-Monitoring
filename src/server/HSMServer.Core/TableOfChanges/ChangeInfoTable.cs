using HSMCommon.Collections;
using HSMDatabase.AccessManager.DatabaseEntities;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.TableOfChanges
{
    internal class ChangeCollection : CDictBase<string, ChangeInfo>
    {
        public ChangeCollection() { }

        public ChangeCollection(Dictionary<string, ChangeInfo> dict) : base(dict) { }
    }


    internal sealed class ChangeInfoTable
    {
        public ChangeCollection Properties { get; } = new();

        public ChangeCollection Settings { get; } = new();

        public ChangeCollection Policies { get; } = new();


        public string Path { get; }


        public ChangeInfoTable() { }

        public ChangeInfoTable(ChangeInfoTableEntity entity)
        {
            static ChangeCollection BuildCollection(Dictionary<string, ChangeInfoEntity> collection) =>
                new(collection.ToDictionary(k => k.Key, v => new ChangeInfo(v.Value)));

            Properties = BuildCollection(entity.Properties);
            Settings = BuildCollection(entity.Settings);
            Policies = BuildCollection(entity.Policies);
        }


        public ChangeInfoTableEntity ToEntity()
        {
            return new();
        }
    }
}