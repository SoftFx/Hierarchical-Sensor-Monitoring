using HSMServer.Authentication;
using HSMServer.Model.Notifications;
using HSMServer.Notifications.Chats;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.Configuration
{
    public class ChatsSettingsViewModel
    {
        public List<ChatViewModel> Chats { get; }


        public ChatsSettingsViewModel() { }

        public ChatsSettingsViewModel(IChatsManager chats, IUserManager userManager = null)
        {
            Chats = chats.GetValues()
                .OrderBy(c => c.Name)
                .Select(c => new ChatViewModel(c, new()))
                .ToList();
        }
    }
}
