﻿using HSMServer.Core.Model;
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
        public const byte TimeToLiveAlertKey = byte.MaxValue;


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

            TimeToLive = new(() => (Parent?.TimeToLive, ParentIsFolder), PredefinedIntervals.ForTimeout);
            KeepHistory = new(() => (Parent?.KeepHistory, ParentIsFolder), PredefinedIntervals.ForKeepHistory);
            SelfDestroy = new(() => (Parent?.SelfDestroy, ParentIsFolder), PredefinedIntervals.ForSelfDestory);

            DataAlerts[TimeToLiveAlertKey] = new();
        }


        internal void Update(BaseNodeModel model)
        {
            Path = model.Path;
            Name = model.DisplayName;
            Description = model.Description;

            TimeToLive.FromModel(model.Settings.TTL.CurValue);
            KeepHistory.FromModel(model.Settings.KeepHistory.CurValue);
            SelfDestroy.FromModel(model.Settings.SelfDestroy.CurValue);

            DataAlerts[TimeToLiveAlertKey].Clear();
            if (TimeToLive.TimeInterval is not TimeInterval.None && model.Policies.TimeToLive is not null) // TODO: remove model.Policies.TimeToLivePolicy null checking after add TTLPolicy for products
                DataAlerts[TimeToLiveAlertKey].Add(new TimeToLiveAlertViewModel(TimeToLive, model.Policies.TimeToLive, model));
        }
    }
}