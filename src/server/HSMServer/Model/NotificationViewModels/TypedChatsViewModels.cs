using System.Collections.Generic;

namespace HSMServer.Model.NotificationViewModels
{
    public abstract class ChatsViewModelBase
    {
        public List<TelegramChatViewModel> Chats { get; }

        public abstract string Title { get; }

        public abstract string NameColumn { get; }


        internal ChatsViewModelBase(List<TelegramChatViewModel> chats)
        {
            Chats = chats;
        }
    }


    public sealed class PrivateChatsViewModel : ChatsViewModelBase
    {
        public override string Title => "Accounts";

        public override string NameColumn => "Username";


        public PrivateChatsViewModel(List<TelegramChatViewModel> chats) : base(chats) { }
    }


    public sealed class GroupChatsViewModel : ChatsViewModelBase
    {
        public override string Title => "Groups";

        public override string NameColumn => "Group name";


        public GroupChatsViewModel(List<TelegramChatViewModel> chats) : base(chats) { }
    }
}
