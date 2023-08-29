using HSMDatabase.AccessManager.DatabaseEntities;
using System;

namespace HSMServer.Core.TableOfChanges
{
    internal class ChangeInfo
    {
        public InitiatorInfo Initiator { get; }

        public DateTime LastUpdate { get; }


        public ChangeInfo()
        {
            Initiator = InitiatorInfo.System;
            LastUpdate = DateTime.UtcNow;
        }

        public ChangeInfo(ChangeInfoEntity entity)
        {
            Initiator = new InitiatorInfo(entity.Initiator);
            LastUpdate = new DateTime(entity.Time);
        }
    }
}