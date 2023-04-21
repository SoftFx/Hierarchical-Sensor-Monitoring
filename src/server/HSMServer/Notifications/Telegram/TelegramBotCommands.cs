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
                    Command = Status,
                    Description = "info about HSM server",
                },
            };
    }
}
