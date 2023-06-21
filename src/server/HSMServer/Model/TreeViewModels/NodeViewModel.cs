using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using HSMServer.Helpers;
using HSMServer.Model.Folders;

namespace HSMServer.Model.TreeViewModel
{
    public enum SensorStatus
    {
        OffTime,
        Ok,
        Warning,
        Error,
        Empty,
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

            bool NodeHasFolder() => Parent is FolderModel;

            ExpectedUpdateInterval = new(model.Settings.TTL.Value, () => Parent?.ExpectedUpdateInterval, NodeHasFolder);
            SavedHistoryPeriod = new(model.Settings.KeepHistory.Value, () => Parent?.SavedHistoryPeriod, NodeHasFolder);
            SelfDestroyPeriod = new(model.Settings.SelfDestroy.Value, () => Parent?.SelfDestroyPeriod, NodeHasFolder);
        }


        internal void Update(BaseNodeModel model)
        {
            Path = model.Path;
            Name = model.DisplayName;
            Description = model.Description;

            UpdatePolicyView(model.Settings.TTL, ExpectedUpdateInterval);
            UpdatePolicyView(model.Settings.KeepHistory, SavedHistoryPeriod);
            UpdatePolicyView(model.Settings.SelfDestroy, SelfDestroyPeriod);
        }


        private static void UpdatePolicyView<T>(SettingProperty<T> property, TimeIntervalViewModel targetView) where T : TimeIntervalModel
        {
            targetView.Update(property.IsSet ? property.Value : null);
        }
    }
}