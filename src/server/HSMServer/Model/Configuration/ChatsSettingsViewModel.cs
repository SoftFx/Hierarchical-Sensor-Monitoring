using HSMServer.Folders;
using HSMServer.Model.Folders;
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

        public ChatsSettingsViewModel(IChatsManager chats, IFolderManager folders)
        {
            var allFolders = folders.GetValues();

            Chats = chats.GetValues()
                .OrderBy(c => c.Name)
                .Select(c => new ChatViewModel(c, BuildChatFolders(c, allFolders)))
                .ToList();
        }

        private static ChatFoldersViewModel BuildChatFolders(Chat chat, List<FolderModel> allFolders)
        {
            var chatFolders = allFolders.Where(f => chat.Folders.Contains(f.Id)).ToList();
            return new(availableFolders: new(), chatFolders);
        }
    }
}
