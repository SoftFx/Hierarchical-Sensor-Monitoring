using System.Collections.Generic;
using Telegram.Bot.Types;

namespace HSMServer.Notifications
{
    internal static class TelegramBotCommands
    {
        internal const string Server = "/server";
        internal const string Start = "/start";
        internal const string Help = "/help";
        internal const string Info = "/info";


        internal static List<BotCommand> Commands { get; } =
            new()
            {
                new BotCommand()
                {
                    Command = Help,
                    Description = "statuses (with icons) ascending priority"
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
            };
    }
}
