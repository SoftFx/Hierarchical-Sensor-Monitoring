using System;
using Telegram.Bot.Types;

namespace HSMServer.Core.Notifications
{
    internal sealed record ChatSettings
    {
        internal Guid Token { get; }

        internal ChatId Chat { get; }


        internal ChatSettings(Guid token)
        {
            Token = token;
        }

        internal ChatSettings(ChatId id)
        {
            Chat = id;
        }
    }
}
