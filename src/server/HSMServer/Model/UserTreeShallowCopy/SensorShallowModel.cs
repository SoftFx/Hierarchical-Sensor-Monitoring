using HSMServer.Core;
using HSMServer.Core.Model;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using System;
using System.Linq;

namespace HSMServer.Model.UserTreeShallowCopy
{
    public sealed class SensorShallowModel : BaseNodeShallowModel<SensorNodeViewModel>
    {
        public override bool IsGrafanaEnabled { get; }

        public override bool IsAccountsEnable { get; }

        public override bool IsAccountsIgnore { get; }


        internal SensorShallowModel(SensorNodeViewModel data, User user) : base(data, user)
        {
            IsGrafanaEnabled = data.Integration.HasGrafana();

            _mutedValue = data.State == SensorState.Muted;

            var notifications = data.RootProduct.Notifications;
            foreach (var (chatId, chat) in notifications.Telegram.Chats)
                GroupsState.Add(chatId, new GroupNotificationsState()
                {
                    Name = chat.Name,
                    IsEnabled = notifications.IsSensorEnabled(data.Id),
                    IsIgnored = notifications.PartiallyIgnored[chatId].TryGetValue(data.Id, out var time) && time != DateTime.MaxValue, // check Muted state instead of time?
                });

            IsAccountsEnable = user.Notifications.IsSensorEnabled(data.Id);
            IsAccountsIgnore = user.Notifications.PartiallyIgnored.Any(s => s.Value.TryGetValue(data.Id, out var groupTime) && groupTime != DateTime.MaxValue); // check Muted state instead of time?
        }
    }
}