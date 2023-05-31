using HSMServer.Model.TreeViewModel;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace HSMServer.Model.UserTreeShallowCopy
{
    public abstract class BaseShallowModel
    {
        internal Dictionary<Telegram.Bot.Types.ChatId, GroupNotificationsState> GroupsState { get; } = new();


        public string GroupsObject => JsonSerializer.Serialize(GroupsState.ToDictionary(k => k.Key?.Identifier ?? 0L, v => v.Value));

        public bool IsGroupsEnable => GroupsState.Values.Any(g => g.IsEnabled);

        public bool IsGroupsIgnore => GroupsState.Values.Any(g => g.IsIgnored);


        public abstract bool CurUserIsManager { get; }

        public abstract bool IsGrafanaEnabled { get; }

        public abstract bool IsAccountsEnable { get; }

        public abstract bool IsAccountsIgnore { get; }


        public abstract string ToJSTree();
    }


    public abstract class BaseShallowModel<T> : BaseShallowModel where T : BaseNodeViewModel
    {
        public T Data { get; }


        protected BaseShallowModel(T data)
        {
            Data = data;
        }
    }
}
