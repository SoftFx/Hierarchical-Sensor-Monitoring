using HSMServer.Model.TreeViewModel;
using System;

namespace HSMServer.Model.UserTreeShallowCopy
{
    public abstract class BaseShallowModel
    {
        public abstract Guid Id { get; }


        public abstract bool CurUserIsManager { get; }

        public abstract bool IsGrafanaEnabled { get; }


        public int ErrorsCount { get; protected set; }

        public string Errors => $"{ErrorsCount} error{(ErrorsCount > 1 ? "s" : string.Empty)}";


        public abstract string ToJSTree();


        public static string GetDisabledJSTree() =>
            $$"""
            {
                "title": "disabled",
                "icon": "disabled",
                "time": "disabled",
                "isManager": "disabled",
                "isGrafanaEnabled": "disabled",
                "isMutedState": "disabled",
                "disabled": {{true.ToString().ToLower()}}
            }
            """;
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
