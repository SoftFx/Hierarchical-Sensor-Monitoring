using HSMServer.Core;
using HSMServer.Core.Model;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
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
            var isMuted = _mutedValue.HasValue && _mutedValue.Value;

            foreach (var (chatId, chat) in notifications.Telegram.Chats)
                GroupsState.Add(chatId, new GroupNotificationsState()
                {
                    Name = chat.Name,
                    IsEnabled = notifications.IsSensorEnabled(data.Id),
                    IsIgnored = !isMuted && notifications.PartiallyIgnored[chatId].ContainsKey(data.Id),
                });

            IsAccountsEnable = user.Notifications.IsSensorEnabled(data.Id);
            IsAccountsIgnore = !isMuted && user.Notifications.PartiallyIgnored.Any(s => s.Value.ContainsKey(data.Id));
        }
    }
}