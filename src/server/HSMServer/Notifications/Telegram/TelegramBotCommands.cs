using System.Collections.Generic;
using Telegram.Bot.Types;

namespace HSMServer.Notifications
{
    internal static class TelegramBotCommands
    {
        internal const string Start = "/start";
        internal const string Info = "/info";
        internal const string Status = "/status";
        internal const string Icons = "/icons";


        internal static List<BotCommand> Commands { get; } =
            new()
            {
                new BotCommand()
                {
                    Command = Info,
                    Description = "get authorized entities settings",
                },
                new BotCommand()
                {
                    Command = Status,
                    Description = "get info about HSM server version and working status",
                },
                new BotCommand()
                {
                    Command = Icons,
                    Description = "get icons list with descriptions"
                }
            };
    }
}
