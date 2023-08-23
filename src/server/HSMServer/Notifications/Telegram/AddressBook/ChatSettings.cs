using HSMServer.Notifications.Telegram;
using System;
using Telegram.Bot.Types;

namespace HSMServer.Notifications
{
    internal sealed record ChatSettings
    {
        internal TelegramChat Chat { get; }

        internal MessageBuilder MessageBuilder { get; } = new();

        internal ChatId ChatId => Chat?.Id;

        internal Guid SystemId => Chat.SystemId;


        internal ChatSettings(TelegramChat chat)
        {
            Chat = chat;
        }
    }
}
