using HSMCommon.Collections;
using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.TableOfChanges
{
    internal class ChangeCollection : CDictBase<string, ChangeInfo>
    {
        public ChangeCollection() { }

        public ChangeCollection(Dictionary<string, ChangeInfo> dict) : base(dict) { }


        public Dictionary<string, ChangeInfoEntity> ToEntity() =>
            this.ToDictionary(k => k.Key, v => v.Value.ToEntity());
    }


    internal class GuidChangeCollection : CDictBase<Guid, ChangeInfo>
    {
        public GuidChangeCollection() { }

        public GuidChangeCollection(Dictionary<Guid, ChangeInfo> dict) : base(dict) { }


        public Dictionary<byte[], ChangeInfoEntity> ToEntity() =>
            this.ToDictionary(k => k.Key.ToByteArray(), v => v.Value.ToEntity());
    }
}
