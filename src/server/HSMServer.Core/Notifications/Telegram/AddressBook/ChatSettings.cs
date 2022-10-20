using HSMServer.Core.Model;
using Telegram.Bot.Types;

namespace HSMServer.Core.Notifications
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
