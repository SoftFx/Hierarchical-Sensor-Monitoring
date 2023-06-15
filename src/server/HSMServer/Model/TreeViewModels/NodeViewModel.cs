using HSMServer.Core.Model;
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

            ExpectedUpdateInterval = new(model.ServerPolicy.ExpectedUpdate.Policy.Interval, () => Parent?.ExpectedUpdateInterval, NodeHasFolder);
            SensorRestorePolicy = new(model.ServerPolicy.RestoreError.Policy.Interval, () => Parent?.SensorRestorePolicy, NodeHasFolder);
            SavedHistoryPeriod = new(model.ServerPolicy.SavedHistoryPeriod.Policy.Interval, () => Parent?.SavedHistoryPeriod, NodeHasFolder);
            SelfDestroyPeriod = new(model.ServerPolicy.SelfDestroy.Policy.Interval, () => Parent?.SelfDestroyPeriod, NodeHasFolder);
        }


        internal void Update(BaseNodeModel model)
        {
            Path = model.Path;
            Name = model.DisplayName;
            Description = model.Description;

            UpdatePolicyView(model.ServerPolicy.ExpectedUpdate, ExpectedUpdateInterval);
            UpdatePolicyView(model.ServerPolicy.RestoreError, SensorRestorePolicy);
            UpdatePolicyView(model.ServerPolicy.SavedHistoryPeriod, SavedHistoryPeriod);
            UpdatePolicyView(model.ServerPolicy.SelfDestroy, SelfDestroyPeriod);
        }


        private static void UpdatePolicyView<T>(CollectionProperty<T> property, TimeIntervalViewModel targetView) where T : ServerPolicy, new()
        {
            targetView.Update(property.IsSet ? property.Policy.Interval : null);
        }
    }
}