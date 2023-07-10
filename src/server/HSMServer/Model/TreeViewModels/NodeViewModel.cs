using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Helpers;
using HSMServer.Model.Folders;

namespace HSMServer.Model.TreeViewModel
{
    public enum SensorStatus
    {
        Empty,
        OffTime,
        Ok,
        Warning,
        Error
    }


    public abstract class NodeViewModel : BaseNodeViewModel
    {
        public string EncodedId { get; }


        public string Path { get; private set; }

        public virtual bool HasData { get; protected set; }

        public BaseNodeViewModel Parent { get; internal set; }


        //TODO: should be changed to NodeViewModel when Sensor will have its own Telegram Settings
        public ProductNodeViewModel RootProduct => Parent is null or FolderModel ? (ProductNodeViewModel)this : ((ProductNodeViewModel)Parent).RootProduct;

        public string FullPath => $"{RootProduct?.Name}{Path}";


        protected NodeViewModel(BaseNodeModel model)
        {
            Id = model.Id;
            Path = model.Path;
            EncodedId = SensorPathHelper.EncodeGuid(model.Id);

            bool ParentIsFolder() => Parent is FolderModel;

            TTL = new(model.Settings.TTL.Value, () => Parent?.TTL, ParentIsFolder);
            KeepHistory = new(model.Settings.KeepHistory.Value, () => Parent?.KeepHistory, ParentIsFolder);
            SelfDestroy = new(model.Settings.SelfDestroy.Value, () => Parent?.SelfDestroy, ParentIsFolder);
        }


        internal void Update(BaseNodeModel model)
        {
            Path = model.Path;
            Name = model.DisplayName;
            Description = model.Description;

            UpdatePolicyView(model.Settings.TTL, TTL);
            UpdatePolicyView(model.Settings.KeepHistory, KeepHistory);
            UpdatePolicyView(model.Settings.SelfDestroy, SelfDestroy);
        }


        private static void UpdatePolicyView<T>(SettingProperty<T> property, TimeIntervalViewModel targetView) where T : TimeIntervalModel
        {
            if (property.IsSet)
                targetView.FromModel(property.Value);
        }
    }
}