using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;
using System.Linq;

namespace HSMServer.Core.Model.NodeSettings
{
    public enum DefaultChatInheritanceMode : byte
    {
        None = 0,
        FromParent = 10,
        FromFolder = 20,
    }


    public sealed class PolicyDestinationSettings : PolicyDestination
    {
        public DefaultChatInheritanceMode InheritanceMode { get; } = DefaultChatInheritanceMode.FromParent;


        public bool IsFromParent => InheritanceMode is DefaultChatInheritanceMode.FromParent;

        public bool IsFromFolder => InheritanceMode is DefaultChatInheritanceMode.FromFolder;


        public PolicyDestinationSettings() : base() { }

        public PolicyDestinationSettings(DefaultChatInheritanceMode mode) : this()
        {
            InheritanceMode = mode;
        }

        public PolicyDestinationSettings(PolicyDestinationSettingsEntity entity) : base(entity)
        {
            InheritanceMode = (DefaultChatInheritanceMode)entity.InheritanceMode;
        }


        public new PolicyDestinationSettingsEntity ToEntity() => new()
        {
            Chats = Chats?.ToDictionary(k => k.Key.ToString(), v => v.Value),
            InheritanceMode = (byte)InheritanceMode,
        };

        public override string ToString() =>
            IsFromFolder ? $"From folder ({base.ToString()})" : IsFromParent ? "From parent" : base.ToString();
    }
}