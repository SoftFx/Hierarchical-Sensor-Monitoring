using HSMServer.Extensions;
using HSMServer.Notifications.Telegram;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Telegram.Bot.Types;

namespace HSMServer.Model.NotificationViewModels
{
    public sealed class ChatsViewModel
    {
        public PrivateChatsViewModel PrivateChats { get; }

        public GroupChatsViewModel GroupChats { get; }


        internal ChatsViewModel(ConcurrentDictionary<ChatId, TelegramChat> privates, ConcurrentDictionary<ChatId, TelegramChat> groups)
        {
            PrivateChats = new PrivateChatsViewModel(privates);
            GroupChats = new GroupChatsViewModel(groups);
        }
    }

    public abstract class ChatsViewModelBase
    {
        public List<TelegramChatViewModel> Chats { get; }

        public abstract string Title { get; }

        public abstract string NameColumn { get; }


        internal ChatsViewModelBase(ConcurrentDictionary<ChatId, TelegramChat> chats)
        {
            Chats = chats.Values.Select(ch => new TelegramChatViewModel(ch)).ToList();
        }
    }

    public sealed class PrivateChatsViewModel : ChatsViewModelBase
    {
        public override string Title => "Accounts";

        public override string NameColumn => "Username";


        public PrivateChatsViewModel(ConcurrentDictionary<ChatId, TelegramChat> chats) : base(chats) { }
    }

    public sealed class GroupChatsViewModel : ChatsViewModelBase
    {
        public override string Title => "Groups";

        public override string NameColumn => "Group name";


        public GroupChatsViewModel(ConcurrentDictionary<ChatId, TelegramChat> chats) : base(chats) { }
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
            ChatId = chat.Id.Identifier ?? 0L;
            IsUserChat = chat.IsUserChat;
            SystemId = chat.SystemId;
            Name = chat.Name;
            AuthorizationTime = chat.AuthorizationTime == DateTime.MinValue
                ? "-"
                : chat.AuthorizationTime.ToDefaultFormat();
        }
    }
}
