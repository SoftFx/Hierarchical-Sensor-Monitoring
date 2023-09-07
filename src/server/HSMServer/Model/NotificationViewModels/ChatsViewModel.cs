using HSMServer.Extensions;
using HSMServer.Notifications;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.NotificationViewModels
{
    public sealed class ChatsViewModel
    {
        public PrivateChatsViewModel PrivateChats { get; }

        public GroupChatsViewModel GroupChats { get; }


        internal ChatsViewModel(List<TelegramChat> chats)
        {
            var privates = new List<TelegramChatViewModel>(1 << 3);
            var groups = new List<TelegramChatViewModel>(1 << 3);

            foreach (var chat in chats)
            {
                var viewModel = new TelegramChatViewModel(chat);

                if (chat.Type is ConnectedChatType.TelegramPrivate)
                    privates.Add(viewModel);
                else
                    groups.Add(viewModel);
            }

            PrivateChats = new PrivateChatsViewModel(privates);
            GroupChats = new GroupChatsViewModel(groups);
        }
    }

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


    public class TelegramChatViewModel
    {
        public string AuthorizationTime { get; }

        public bool IsUserChat { get; }

        public Guid SystemId { get; }

        public long ChatId { get; }

        public string Name { get; }


        public TelegramChatViewModel(TelegramChat chat)
        {
            ChatId = chat.ChatId.Identifier ?? 0L;
            IsUserChat = chat.IsUserChat;
            SystemId = chat.Id;
            Name = chat.Name;
            AuthorizationTime = chat.AuthorizationTime == DateTime.MinValue
                ? "-"
                : chat.AuthorizationTime.ToDefaultFormat();
        }
    }
}
