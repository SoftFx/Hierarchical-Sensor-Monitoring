using System.Collections.Generic;
using Telegram.Bot.Types;

namespace HSMServer.Notifications
{
    internal static class TelegramBotCommands
    {
        internal const string StartBotCommand = "/start";
        internal const string InfoBotCommand = "/info";
        internal const string StatusBotCommand = "/status";


        internal static List<BotCommand> BuildCommands() =>
            new()
            {
                new BotCommand()
                {
                    Command = InfoBotCommand,
                    Description = "get authorized entities settings",
                },
                new BotCommand()
                {
                    Command = StatusBotCommand,
                    Description = "get info about HSM server version and working status",
                }
            };
    }
}
