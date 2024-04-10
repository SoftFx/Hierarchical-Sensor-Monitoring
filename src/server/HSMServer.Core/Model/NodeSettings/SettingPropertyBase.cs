namespace HSMServer.Core.Model.NodeSettings
{
    public abstract class SettingPropertyBase
    {
        internal SettingPropertyBase ParentProperty { get; set; }


        public abstract bool IsEmpty { get; }

        public abstract bool IsSet { get; }
    }


    public abstract class SettingPropertyBase<TModel> : SettingPropertyBase where TModel : class, new()
    {
        protected abstract TModel EmptyValue { get; }


        public override bool IsEmpty => Value is null;


        public TModel CurValue { get; private set; } = new TModel();

        public TModel Value => IsSet ? CurValue : ((SettingPropertyBase<TModel>)ParentProperty)?.Value ?? EmptyValue;


        internal bool TrySetValue(TModel newValue)
        {
            if (newValue is not null && CurValue.ToString() != newValue.ToString())
            {
                CurValue = newValue;

                return true;
            }

            return false;
        }
    }
}