using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.NodeSettings
{
    public sealed class PolicyDestinationSettings : PolicyDestination
    {
        public bool IsFromParent { get; private set; }


        public PolicyDestinationSettings() : base() { }

        public PolicyDestinationSettings(bool isFromParent, Dictionary<Guid, string> chats)
        {
            IsFromParent = isFromParent;

            foreach (var chat in chats)
                Chats.Add(chat.Key, chat.Value);
        }

        public PolicyDestinationSettings(PolicyDestinationSettingsEntity entity) : base(entity)
        {
            IsFromParent = entity.IsFromParent;
        }


        internal new PolicyDestinationSettingsEntity ToEntity() => new()
        {
            Chats = Chats?.ToDictionary(k => k.Key.ToString(), v => v.Value),
            UseDefaultChats = UseDefaultChats,
            IsFromParent = IsFromParent,
            AllChats = AllChats,
        };

        public override string ToString() =>
            IsFromParent ? "From parent" : base.ToString();
    }
}