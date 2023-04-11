using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Extensions;
using HSMServer.Helpers;
using HSMServer.Model.Folders;
using System;

namespace HSMServer.Model.TreeViewModel
{
    public enum SensorStatus
    {
        OffTime,
        Ok,
        Warning,
        Error,
    }


    public abstract class NodeViewModel : BaseNodeViewModel
    {
        public string EncodedId { get; }


        public string Path { get; private set; }

        public DateTime UpdateTime { get; protected set; }

        public SensorStatus Status { get; protected set; }

        public virtual bool HasData { get; protected set; }


        public string Tooltip => $"{Name}{Environment.NewLine}{(UpdateTime != DateTime.MinValue ? UpdateTime.ToDefaultFormat() : "no data")}";

        public string Title => Name?.Replace('\\', ' ') ?? string.Empty;

        public string FullPath => $"{RootProduct?.Name}{Path}";


        protected NodeViewModel(BaseNodeModel model)
        {
            Id = model.Id;
            Path = model.Path;
            EncodedId = SensorPathHelper.EncodeGuid(model.Id);

            bool NodeHasFolder() => Parent is FolderModel;

            ExpectedUpdateInterval = new(model.ServerPolicy.ExpectedUpdate.Policy.Interval, () => Parent?.ExpectedUpdateInterval, NodeHasFolder);
            SensorRestorePolicy = new(model.ServerPolicy.RestoreError.Policy.Interval, () => Parent?.SensorRestorePolicy, NodeHasFolder);
        }


        internal void Update(BaseNodeModel model)
        {
            Path = model.Path;
            Name = model.DisplayName;
            Description = model.Description;

            UpdatePolicyView(model.ServerPolicy.ExpectedUpdate, ExpectedUpdateInterval);
            UpdatePolicyView(model.ServerPolicy.RestoreError, SensorRestorePolicy);
        }


        private static void UpdatePolicyView<T>(CollectionProperty<T> property, TimeIntervalViewModel targetView) where T : ServerPolicy, new()
        {
            targetView.Update(property.IsSet ? property.Policy.Interval : null);
        }
    }
}