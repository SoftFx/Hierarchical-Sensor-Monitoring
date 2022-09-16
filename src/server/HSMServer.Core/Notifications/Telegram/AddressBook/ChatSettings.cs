using HSMServer.Core.Model;
using System;
using Telegram.Bot.Types;

namespace HSMServer.Core.Notifications
{
    internal sealed record ChatSettings
    {
        internal Guid User { get; }

        internal Guid Token { get; }

        internal string Username { get; }

        internal TelegramChat Chat { get; }

        internal MessageBuilder MessageBuilder { get; } = new();


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
