namespace HSMServer.Model.Controls
{
    public abstract record SensorSettingControlBase<TParent>
        where TParent : SensorSettingControlBase<TParent>
    {
        private protected readonly ParentRequest _parentRequest;


        internal delegate (TParent Value, bool IsFolder) ParentRequest();


        internal TParent Parent => _parentRequest?.Invoke().Value;

        internal bool HasFolder => _parentRequest?.Invoke().IsFolder ?? false;

        internal bool HasParent => Parent is not null;


        internal SensorSettingControlBase() { }

        internal SensorSettingControlBase(ParentRequest parentRequest)
        {
            _parentRequest = parentRequest;
        }
    }
}
