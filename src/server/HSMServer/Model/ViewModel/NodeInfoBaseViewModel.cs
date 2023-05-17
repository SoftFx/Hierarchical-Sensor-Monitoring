using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using System;
using System.ComponentModel.DataAnnotations;
using HSMServer.Attributes;
using HSMServer.Extensions;


namespace HSMServer.Model.ViewModel
{
    public abstract class NodeInfoBaseViewModel
    {
        public string Header { get; }

        public Guid RootProductId { get; }

        public DateTime LastUpdateTime { get; set; }

        public SensorStatus Status { get; set; }

        [Display(Name = "Time to live interval")]
        [MinTimeInterval(TimeInterval.OneMinute, ErrorMessage = "{0} minimal value is {1}.")]
        public TimeIntervalViewModel ExpectedUpdateInterval { get; set; }

        [Display(Name = "Sensitivity interval")]
        [MinTimeInterval(TimeInterval.OneMinute, ErrorMessage = "{0} minimal value is {1}.")]
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
            Status = model.Status.ToEmpty(model.UpdateTime != DateTime.MinValue);
            Description = model.Description;
            LastUpdateTime = model.UpdateTime;

            ExpectedUpdateInterval = new(model.ExpectedUpdateInterval, PredefinedIntervals.ForTimeout);
            SensorRestorePolicy = new(model.SensorRestorePolicy, PredefinedIntervals.ForRestore);
        }
    }
}
