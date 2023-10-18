using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace HSMServer.Extensions
{
    internal static class TelegramExtensions
    {
        public static bool FromPrivateChat(this Message message)
        {
            return message?.Chat?.Type == ChatType.Private;
        }
    }
}
