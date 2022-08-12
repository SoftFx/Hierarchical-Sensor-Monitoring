using Telegram.Bot.Types;

namespace HSMServer.Core.Notifications
{
    internal sealed class ChatSettings
    {
        internal InvitationToken Token { get; }

        internal ChatId Chat { get; set; }


        internal ChatSettings(InvitationToken token)
        {
            Token = token;
        }
    }
}
