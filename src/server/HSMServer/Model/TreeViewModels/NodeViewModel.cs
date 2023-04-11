using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Extensions;
using HSMServer.Helpers;
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


    public abstract class NodeViewModel
    {
        public TimeIntervalViewModel ExpectedUpdateInterval { get; }

        public TimeIntervalViewModel SensorRestorePolicy { get; }


        public Guid Id { get; }

        public string EncodedId { get; }


        public string FullPath => $"{RootProduct?.Name}{Path}";

        public ProductNodeViewModel RootProduct => Parent?.RootProduct ?? (ProductNodeViewModel)this;


        public string Name { get; private set; }

        public string Path { get; private set; }



        public string Description { get; protected set; }

        public DateTime UpdateTime { get; protected set; }

        public SensorStatus Status { get; protected set; }


        public virtual bool HasData { get; protected set; }


        public NodeViewModel Parent { get; internal set; }


        public string Tooltip => $"{Name}{Environment.NewLine}{(UpdateTime != DateTime.MinValue ? UpdateTime.ToDefaultFormat() : "no data")}";

        public string Title => Name?.Replace('\\', ' ') ?? string.Empty;


        protected NodeViewModel(BaseNodeModel model)
        {
            Id = model.Id;
            Path = model.Path;

            EncodedId = SensorPathHelper.EncodeGuid(model.Id);

            ExpectedUpdateInterval = new(model.ServerPolicy.ExpectedUpdate.Policy.Interval, () => Parent?.ExpectedUpdateInterval);
            SensorRestorePolicy = new(model.ServerPolicy.RestoreError.Policy.Interval, () => Parent?.SensorRestorePolicy);
        }


        internal void Update(BaseNodeModel model)
        {
            Name = model.DisplayName;
            Path = model.Path;
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