using HSMServer.Core.Model;
using HSMServer.Helpers;
using HSMServer.Model.DataAlerts;
using HSMServer.Model.Folders;

namespace HSMServer.Model.TreeViewModel
{
    public enum SensorStatus
    {
        Empty,
        OffTime,
        Ok,
        Error
    }


    public abstract class NodeViewModel : BaseNodeViewModel
    {
        public string EncodedId { get; }


        public BaseNodeViewModel Parent { get; internal set; }

        public virtual bool HasData { get; protected set; }

        public string Path { get; private set; }


        //TODO: should be changed to NodeViewModel when Sensor will have its own Telegram Settings
        public ProductNodeViewModel RootProduct => Parent is null or FolderModel ? (ProductNodeViewModel)this : ((ProductNodeViewModel)Parent).RootProduct;

        public string FullPath => $"{RootProduct?.Name}{Path}";

        private bool ParentIsFolder => Parent is FolderModel;


        protected NodeViewModel(BaseNodeModel model)
        {
            Id = model.Id;
            Path = model.Path;
            EncodedId = SensorPathHelper.EncodeGuid(model.Id);

            TimeToLive = new(() => (Parent?.TimeToLive, ParentIsFolder));
            KeepHistory = new(() => (Parent?.KeepHistory, ParentIsFolder));
            SelfDestroy = new(() => (Parent?.SelfDestroy, ParentIsFolder));
        }


        internal void Update(BaseNodeModel model)
        {
            Path = model.Path;
            Name = model.DisplayName;
            Description = model.Description;

            TimeToLive.FromModel(model.Settings.TTL.CurValue, PredefinedIntervals.ForTimeout);
            KeepHistory.FromModel(model.Settings.KeepHistory.CurValue, PredefinedIntervals.ForKeepHistory);
            SelfDestroy.FromModel(model.Settings.SelfDestroy.CurValue, PredefinedIntervals.ForSelfDestory);

            TimeToLiveAlert = new TimeToLiveAlertViewModel(model.Policies.TimeToLive, model);
        }
    }
}