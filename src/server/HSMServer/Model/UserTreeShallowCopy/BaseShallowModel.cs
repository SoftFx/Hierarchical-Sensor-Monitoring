﻿using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.UserTreeShallowCopy
{
    public abstract class BaseShallowModel<T> where T : NodeViewModel
    {
        private readonly bool _curUserIsManager;


        public T Data { get; }

        public bool IsIgnoredState { get; protected set; }

        public NodeShallowModel Parent { get; internal set; }

        
        public abstract bool IsAccountsEnable { get; }

        public abstract bool IsGroupsEnable { get; }

        
        protected BaseShallowModel(T data, User user)
        {
            _curUserIsManager = user.IsManager(data.Parent?.Id ?? data.Id);

            Data = data;
        }


        public string ToJSTree() =>
        $$"""
        {
            "title": "{{Data.Title}}",
            "icon": "{{Data.Status.ToIcon()}}",
            "time": "{{Data.UpdateTime.ToDefaultFormat()}}",
            "isManager": "{{_curUserIsManager}}",
            "isAccountsEnable": "{{IsAccountsEnable}}",
            "isGroupsEnable": "{{IsGroupsEnable}}",
            "isIgnoredState": "{{IsIgnoredState}}"
        }
        """;
    }
}
