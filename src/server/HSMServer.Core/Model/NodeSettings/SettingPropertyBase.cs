namespace HSMServer.Core.Model.NodeSettings
{
    public abstract class SettingPropertyBase<T> where T : class, new()
    {
        private SettingPropertyBase<T> _parent;


        protected abstract T EmptyValue { get; }

        public abstract bool IsSet { get; }


        public T CurValue { get; private set; } = new T();


        public T Value => IsSet ? CurValue : _parent?.Value ?? EmptyValue;

        public bool IsEmpty => Value is null;


        internal bool TrySetValue(T newValue)
        {
            if (newValue is not null && CurValue.ToString() != newValue.ToString())
            {
                CurValue = newValue;

                return true;
            }

            return false;
        }

        internal void SetParent(SettingPropertyBase<T> parent) => _parent = parent;
    }
}