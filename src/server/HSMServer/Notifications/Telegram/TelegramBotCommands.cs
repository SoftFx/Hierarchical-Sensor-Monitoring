using System.Collections.Generic;
using Telegram.Bot.Types;

namespace HSMServer.Notifications
{
    internal static class TelegramBotCommands
    {
        internal const string StatusPriority = "/status_priority";
        internal const string Server = "/server";
        internal const string Icons = "/icons";
        internal const string Start = "/start";
        internal const string Info = "/info";


        internal static List<BotCommand> Commands { get; } =
            new()
            {
                new BotCommand()
                {
                    Command = Icons,
                    Description = "icons list with descriptions"
                },
                new BotCommand()
                {
                    Command = Info,
                    Description = "authorized entities",
                },
                new BotCommand()
                {
                    Command = Server,
                    Description = "info about HSM server",
                },
                new BotCommand()
                {
                    Command = StatusPriority,
                    Description = "statuses ascending priority",
                },
            };
    }
}
