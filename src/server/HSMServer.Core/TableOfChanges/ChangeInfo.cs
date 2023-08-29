using HSMDatabase.AccessManager.DatabaseEntities;
using System;

namespace HSMServer.Core.TableOfChanges
{
    internal class ChangeInfo
    {
        public InitiatorInfo Initiator { get; } = InitiatorInfo.System;

        public DateTime LastUpdate { get; } = DateTime.UtcNow;


        public ChangeInfo() { }

        public ChangeInfo(ChangeInfoEntity entity)
        {
            entity ??= new ChangeInfoEntity();

            Initiator = new InitiatorInfo(entity.Initiator);
            LastUpdate = new DateTime(entity.Time);
        }


        public bool CanChange(InitiatorInfo newInfo) => Initiator.Type <= newInfo.Type;

        public ChangeInfoEntity ToEntity() => new()
        {
            Initiator = Initiator.ToEntity(),
            Time = LastUpdate.Ticks,
        };
    }
}