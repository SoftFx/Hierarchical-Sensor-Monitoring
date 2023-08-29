using HSMDatabase.AccessManager.DatabaseEntities;
using System;

namespace HSMServer.Core.TableOfChanges
{
    internal class ChangeInfo
    {
        public InitiatorInfo Initiator { get; private set; } = InitiatorInfo.System;

        public DateTime LastUpdate { get; private set; } = DateTime.UtcNow;


        public ChangeInfo() { } // for CDict collection

        public ChangeInfo(ChangeInfoEntity entity)
        {
            entity ??= new ChangeInfoEntity();

            Initiator = new InitiatorInfo(entity.Initiator);
            LastUpdate = new DateTime(entity.Time);
        }


        internal void SetUpdate(InitiatorInfo initiator)
        {
            Initiator = initiator;
            LastUpdate = DateTime.UtcNow;
        }

        public bool CanChange(InitiatorInfo newInfo) => Initiator.Type <= newInfo.Type;

        public ChangeInfoEntity ToEntity() => new()
        {
            Initiator = Initiator.ToEntity(),
            Time = LastUpdate.Ticks,
        };
    }
}