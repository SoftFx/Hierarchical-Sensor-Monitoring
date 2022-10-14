using HSMServer.Core.Model;
using System;
using Telegram.Bot.Types;

namespace HSMServer.Core.Notifications
{
    internal sealed record ChatSettings
    {
        internal Guid User { get; }

        internal TelegramChat Chat { get; }

        internal MessageBuilder MessageBuilder { get; } = new();

        internal ChatId ChatId => Chat?.Id;


        internal ChatSettings(Guid user, TelegramChat chat)
        {
            User = user;
            Chat = chat;
        }
    }
}
