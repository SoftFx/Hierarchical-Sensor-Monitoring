using System.ComponentModel.DataAnnotations;

namespace HSMServer.Notifications
{
    public enum ConnectedChatType : byte
    {
        [Display(Name = "direct")]
        TelegramPrivate = 0,

        [Display(Name = "group")]
        TelegramGroup = 1,
    }
}
