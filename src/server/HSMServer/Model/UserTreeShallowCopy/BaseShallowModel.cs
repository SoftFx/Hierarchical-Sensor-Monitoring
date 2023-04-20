using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.UserTreeShallowCopy
{
    public abstract class BaseShallowModel
    {
        public abstract bool CurUserIsManager { get; }

        public abstract bool IsAccountsEnable { get; }

        public abstract bool IsGroupsEnable { get; }


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
