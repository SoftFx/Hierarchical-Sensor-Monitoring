using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace HSMServer.Model.UserTreeShallowCopy
{
    public abstract class BaseShallowModel
    {
        internal Dictionary<Telegram.Bot.Types.ChatId, GroupNotificationsState> GroupsState { get; } = new();


        protected string GroupsJsonDict => JsonSerializer.Serialize(GroupsState.ToDictionary(k => k.Key?.Identifier ?? 0L, v => v.Value));

        public bool IsGroupsEnable => GroupsState.Count > 0 && GroupsState.Values.All(g => g.IsEnabled);

        public bool IsGroupsIgnore => GroupsState.Count > 0 && GroupsState.Values.All(g => g.IsIgnored);


        public abstract Guid Id { get; }


        public abstract bool CurUserIsManager { get; }

        public abstract bool IsGrafanaEnabled { get; }

        public abstract bool IsAccountsEnable { get; }

        public abstract bool IsAccountsIgnore { get; }


        public abstract string ToJSTree();


        public static string GetDisabledJSTree() =>
            $$"""
            {
                "title": "disabled",
                "icon": "disabled",
                "time": "disabled",
                "isManager": "disabled",
                "isGrafanaEnabled": "disabled",
                "isAccountsEnable": "disabled",
                "groups": "disabled",
                "isMutedState": "disabled",
                "disabled": {{true.ToString().ToLower()}}
            }
            """;


        protected void UpdateGroupsState(BaseShallowModel model)
        {
            foreach (var (chatId, groupInfo) in model.GroupsState)
            {
                if (GroupsState.TryGetValue(chatId, out var info))
                    info.CalculateState(groupInfo);
                else
                    GroupsState.Add(chatId, new GroupNotificationsState()
                    {
                        Name = groupInfo.Name,
                        IsEnabled = groupInfo.IsEnabled,
                        IsIgnored = groupInfo.IsIgnored
                    });
            }
        }
    }


    public abstract class BaseShallowModel<T> : BaseShallowModel where T : BaseNodeViewModel
    {
        public T Data { get; }

        public override Guid Id => Data.Id;


        protected BaseShallowModel(T data)
        {
            Data = data;
        }
    }
}
