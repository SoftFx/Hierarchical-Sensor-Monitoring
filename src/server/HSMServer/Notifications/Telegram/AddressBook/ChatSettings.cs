using HSMServer.Notifications.Telegram;
using Telegram.Bot.Types;

namespace HSMServer.Notifications
{
    internal sealed record ChatSettings
    {
        internal TelegramChat Chat { get; }

        internal MessageBuilder MessageBuilder { get; } = new();

        internal ChatId ChatId => Chat?.Id;


        internal ChatSettings(TelegramChat chat)
        {
            Chat = chat;
        }
    }
}
