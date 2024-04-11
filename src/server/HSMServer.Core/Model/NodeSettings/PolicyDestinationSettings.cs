using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies;
using System.Linq;

namespace HSMServer.Core.Model.NodeSettings
{
    public sealed class PolicyDestinationSettings : PolicyDestination
    {
        public bool IsFromParent { get; private set; }


        public PolicyDestinationSettings() : base() { }

        internal PolicyDestinationSettings(PolicyDestinationSettingsEntity entity) : base(entity)
        {
            IsFromParent = entity.IsFromParent;
        }


        internal void Update(PolicyDestinationSettingsUpdate update)
        {
            IsFromParent = update?.IsFromParent ?? IsFromParent;

            base.Update(update);
        }

        internal new PolicyDestinationSettingsEntity ToEntity() => new()
        {
            Chats = Chats?.ToDictionary(k => k.Key.ToString(), v => v.Value),
            UseDefaultChats = UseDefaultChats,
            IsFromParent = IsFromParent,
            AllChats = AllChats,
        };

        public override string ToString() =>
            IsFromParent ? "Is from parent" : base.ToString();
    }
}