using HSMServer.Authentication;
using HSMServer.Notifications;
using System.Collections.Generic;

namespace HSMServer.Model.NotificationViewModels
{
    public sealed class ChatsViewModel
    {
        public PrivateChatsViewModel PrivateChats { get; }

        public GroupChatsViewModel GroupChats { get; }


        internal ChatsViewModel(List<TelegramChat> chats, TreeViewModel.TreeViewModel tree, IUserManager userManager)
        {
            var privates = new List<TelegramChatViewModel>(1 << 3);
            var groups = new List<TelegramChatViewModel>(1 << 3);

            foreach (var chat in chats)
            {
                var viewModel = new TelegramChatViewModel(chat, tree, userManager);

                if (chat.Type is ConnectedChatType.TelegramPrivate)
                    privates.Add(viewModel);
                else
                    groups.Add(viewModel);
            }

            PrivateChats = new PrivateChatsViewModel(privates);
            GroupChats = new GroupChatsViewModel(groups);
        }
    }
}
