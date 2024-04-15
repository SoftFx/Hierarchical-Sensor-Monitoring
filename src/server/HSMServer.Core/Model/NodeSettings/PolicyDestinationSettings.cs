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

        public PolicyDestinationSettings(PolicyDestinationSettingsEntity entity) : base(entity)
        {
            IsFromParent = entity.IsFromParent;
        }


        public PolicyDestinationSettings Initialize(Dictionary<Guid, string> chats = null, bool isFromParent = false)
        {
            IsFromParent = isFromParent;

            if (chats is not null)
                foreach (var chat in chats)
                    Chats.Add(chat.Key, chat.Value);

            return this;
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