using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Reflection;

namespace HSMServer.Core.TableOfChanges
{
    internal class ChangeInfo
    {
        private const int CurrentPropertyVersion = 1;


        public InitiatorInfo Initiator { get; private set; } = InitiatorInfo.System;

        public DateTime LastUpdate { get; private set; } = DateTime.UtcNow;

        public int PropertyVersion { get; private set; }


        public bool NeedMigrate => CurrentPropertyVersion > PropertyVersion;


        public ChangeInfo() { } // for CDict collection

        public ChangeInfo(ChangeInfoEntity entity)
        {
            entity ??= new ChangeInfoEntity();

            Initiator = new InitiatorInfo(entity.Initiator);
            LastUpdate = new DateTime(entity.Time);
            PropertyVersion = entity.PropertyVersion;
        }


        internal void SetUpdate(InitiatorInfo initiator)
        {
            Initiator = initiator;
            LastUpdate = DateTime.UtcNow;
            PropertyVersion = CurrentPropertyVersion;
        }

        public bool CanChange(InitiatorInfo newInfo) => newInfo.IsForceUpdate || Initiator.Type <= newInfo.Type;

        public ChangeInfoEntity ToEntity() => new()
        {
            Initiator = Initiator.ToEntity(),
            PropertyVersion = PropertyVersion,
            Time = LastUpdate.Ticks,
        };
    }
}