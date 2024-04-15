using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;
using System.Linq;

namespace HSMServer.Core.Model.NodeSettings
{
    public sealed class PolicyDestinationSettings : PolicyDestination
    {
        public bool IsFromParent { get; }


        public PolicyDestinationSettings() : base() { }

        public PolicyDestinationSettings(PolicyDestinationSettingsEntity entity) : base(entity)
        {
            IsFromParent = entity.IsFromParent;
        }


        public new PolicyDestinationSettingsEntity ToEntity() => new()
        {
            Chats = Chats?.ToDictionary(k => k.Key.ToString(), v => v.Value),
            IsFromParent = IsFromParent,
        };

        public override string ToString() =>
            IsFromParent ? "From parent" : base.ToString();
    }
}