using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using System;
using HSMServer.Attributes;


namespace HSMServer.Model.ViewModel
{
    public abstract class NodeInfoBaseViewModel
    {
        public string Header { get; }

        public Guid RootProductId { get; }

        public DateTime LastUpdateTime { get; set; }

        public SensorStatus Status { get; set; }

        [CustomTimeIntervalMinValue(600000000, ErrorMessage = "Time to live interval minimal value is 1 min")]
        public TimeIntervalViewModel ExpectedUpdateInterval { get; set; }

        [CustomTimeIntervalMinValue(600000000, ErrorMessage = "Sensitivity interval minimal value is 1 min")]
        public TimeIntervalViewModel SensorRestorePolicy { get; set; }


        public string EncodedId { get; set; }

        public string Description { get; set; }


        public NodeInfoBaseViewModel() { }

        internal NodeInfoBaseViewModel(NodeViewModel model) : this((BaseNodeViewModel)model)
        {
            EncodedId = model.EncodedId;
            Header = $"{model.RootProduct.Name}{model.Path}";
            RootProductId = model.RootProduct.Id;
        }

        internal NodeInfoBaseViewModel(FolderModel model) : this((BaseNodeViewModel)model)
        {
            EncodedId = model.Id.ToString();
            Header = model.Name;
        }

        private NodeInfoBaseViewModel(BaseNodeViewModel model)
        {
            Status = model.Status;
            Description = model.Description;
            LastUpdateTime = model.UpdateTime;

            ExpectedUpdateInterval = new(model.ExpectedUpdateInterval, PredefinedIntervals.ForTimeout);
            SensorRestorePolicy = new(model.SensorRestorePolicy, PredefinedIntervals.ForRestore);
        }
    }
}
